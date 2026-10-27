using System.Text;
using CSJS.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace CSJS.LanguageService;

/// <summary>
/// C# IntelliSense for one CSJS project, over a Roslyn <see cref="AdhocWorkspace"/>.
/// <para>
/// One implementation serves every editor: <c>cs2js lsp</c> wraps it in JSON-RPC for VS Code and
/// any other LSP client, and CSJSGen calls it in-process for Monaco. The workspace is compiled
/// exactly as the transpiler compiles a build — same references, same parse options, the
/// <c>CSJS</c> symbol defined — and the transpiler itself runs for diagnostics, so the editor and
/// <c>cs2js build</c> cannot disagree about what is an error.
/// </para>
/// <para>
/// Thread-safe. Document updates are serialised; queries run against an immutable snapshot, so a
/// slow completion never blocks typing and a stale one is simply cancelled by the caller.
/// </para>
/// </summary>
public sealed class CsjsLanguageService : IDisposable
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly object _gate = new();
    private readonly LanguageServiceOptions _options;
    private readonly AdhocWorkspace _workspace;
    private readonly ProjectId _projectId;
    private readonly Dictionary<string, DocumentId> _documents = new(PathComparer);

    private int _version;
    private TranspileRun? _transpile;
    private CachedCompletion? _lastCompletion;
    private int _nextCompletionId;

    public CsjsLanguageService(LanguageServiceOptions? options = null)
    {
        _options = options ?? new LanguageServiceOptions();

        // The default MEF host discovers the Features assemblies, which is where completion,
        // quick info, and the rest of the IDE services live.
        _workspace = new AdhocWorkspace(MefHostServices.DefaultHost);
        _projectId = ProjectId.CreateNewId("csjs");

        var parseOptions = new CSharpParseOptions(
            languageVersion: LanguageVersion.Preview,
            documentationMode: DocumentationMode.Parse,
            preprocessorSymbols: _options.DefineConstants.Concat(new[] { "CSJS" }));

        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: false,
            nullableContextOptions: NullableContextOptions.Enable,
            concurrentBuild: false,
            metadataImportOptions: MetadataImportOptions.Public);

        _workspace.AddProject(ProjectInfo.Create(
            _projectId,
            VersionStamp.Create(),
            _options.AssemblyName,
            _options.AssemblyName,
            LanguageNames.CSharp,
            compilationOptions: compilationOptions,
            parseOptions: parseOptions,
            metadataReferences: ReferenceSet.For(_options.AdditionalReferences)));
    }

    /// <summary>Increments on every document change; callers use it to drop stale results.</summary>
    public int Version
    {
        get { lock (_gate) return _version; }
    }

    /// <summary>Full paths of every document the service holds.</summary>
    public IReadOnlyList<string> DocumentPaths
    {
        get { lock (_gate) return _documents.Keys.ToList(); }
    }

    /// <summary>True when the service holds a document at this path.</summary>
    public bool Contains(string path)
    {
        lock (_gate) return _documents.ContainsKey(Normalize(path));
    }

    /// <summary>The service's current text for a document, or null when it has none.</summary>
    public string? GetText(string path)
    {
        var document = Snapshot(path);
        return document is not null && document.TryGetText(out var text) ? text.ToString() : null;
    }

    // -----------------------------------------------------------------------------------------
    // Documents
    // -----------------------------------------------------------------------------------------

    /// <summary>Adds a document, or replaces its text when it is already present.</summary>
    public void UpdateDocument(string path, string text)
    {
        var full = Normalize(path);
        lock (_gate)
        {
            var solution = _workspace.CurrentSolution;
            var source = SourceText.From(text, Encoding.UTF8);

            if (_documents.TryGetValue(full, out var id))
            {
                // Unchanged text is common — a save, a re-sync after focus — and recompiling for
                // it would throw away every cached semantic model for nothing.
                if (solution.GetDocument(id) is { } existing
                    && existing.TryGetText(out var current)
                    && current.ContentEquals(source))
                {
                    return;
                }

                solution = solution.WithDocumentText(id, source);
            }
            else
            {
                id = DocumentId.CreateNewId(_projectId, full);
                solution = solution.AddDocument(DocumentInfo.Create(
                    id,
                    Path.GetFileName(full),
                    loader: TextLoader.From(TextAndVersion.Create(source, VersionStamp.Create(), full)),
                    filePath: full,
                    sourceCodeKind: SourceCodeKind.Regular));
                _documents[full] = id;
            }

            _workspace.TryApplyChanges(solution);
            _version++;
        }
    }

    /// <summary>Removes a document. Missing paths are ignored.</summary>
    public void RemoveDocument(string path)
    {
        var full = Normalize(path);
        lock (_gate)
        {
            if (!_documents.Remove(full, out var id)) return;
            _workspace.TryApplyChanges(_workspace.CurrentSolution.RemoveDocument(id));
            _version++;
        }
    }

    /// <summary>Replaces the whole document set — used when a project is (re)loaded.</summary>
    public void SetDocuments(IEnumerable<KeyValuePair<string, string>> documents)
    {
        var incoming = documents.ToDictionary(d => Normalize(d.Key), d => d.Value, PathComparer);
        lock (_gate)
        {
            foreach (var stale in _documents.Keys.Where(k => !incoming.ContainsKey(k)).ToList())
                RemoveDocument(stale);
        }

        foreach (var (path, text) in incoming)
            UpdateDocument(path, text);
    }

    // -----------------------------------------------------------------------------------------
    // Completion
    // -----------------------------------------------------------------------------------------

    /// <summary>Roslyn's completion list at a position, or null when the document is unknown.</summary>
    public async Task<CompletionResult?> GetCompletionsAsync(string path, TextPosition position, CancellationToken cancellationToken = default)
    {
        var document = Snapshot(path);
        if (document is null) return null;

        var service = CompletionService.GetService(document);
        if (service is null) return null;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var offset = ToOffset(text, position);

        var list = await service.GetCompletionsAsync(document, offset, cancellationToken: cancellationToken).ConfigureAwait(false);

        var entries = new List<CompletionEntry>(list.ItemsList.Count);
        for (var i = 0; i < list.ItemsList.Count; i++)
        {
            var item = list.ItemsList[i];

            // Roslyn's snippet items expand only through its own change pipeline, which an editor
            // client cannot run. The keyword items cover the same words.
            if (item.Tags.Contains(WellKnownTags.Snippet)) continue;

            var insert = item.Properties.TryGetValue("InsertionText", out var insertion) && insertion.Length > 0
                ? insertion
                : item.DisplayText;

            entries.Add(new CompletionEntry(
                Label: item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix,
                InsertText: insert,
                FilterText: item.FilterText,
                SortText: item.SortText,
                Detail: string.IsNullOrEmpty(item.InlineDescription) ? null : item.InlineDescription,
                Kind: KindOf(item.Tags),
                ReplaceRange: ToRange(text, item.Span),
                Index: i,
                Preselect: item.Rules.MatchPriority == MatchPriority.Preselect));
        }

        var id = Interlocked.Increment(ref _nextCompletionId);
        lock (_gate) _lastCompletion = new CachedCompletion(id, document, list);

        return new CompletionResult(id, entries);
    }

    /// <summary>
    /// The signature and documentation for one item of the most recent completion list. Computed
    /// lazily, because describing a thousand items to show one is the classic slow-IntelliSense
    /// mistake.
    /// </summary>
    public async Task<CompletionDetails?> ResolveCompletionAsync(int resultId, int index, CancellationToken cancellationToken = default)
    {
        CachedCompletion? cached;
        lock (_gate) cached = _lastCompletion;

        if (cached is null || cached.Id != resultId) return null;
        if (index < 0 || index >= cached.List.ItemsList.Count) return null;

        var service = CompletionService.GetService(cached.Document);
        if (service is null) return null;

        var description = await service
            .GetDescriptionAsync(cached.Document, cached.List.ItemsList[index], cancellationToken)
            .ConfigureAwait(false);
        if (description is null || description.TaggedParts.IsDefaultOrEmpty) return null;

        // The first line is the signature; anything after the first break is documentation.
        var parts = description.TaggedParts;
        var breakAt = parts.IndexOf(parts.FirstOrDefault(p => p.Tag == TextTags.LineBreak));
        if (breakAt < 0) return new CompletionDetails(description.Text, null);

        var signature = string.Concat(parts.Take(breakAt).Select(p => p.Text));
        var documentation = string.Concat(parts.Skip(breakAt + 1).Select(p => p.Text)).Trim();
        return new CompletionDetails(signature, documentation.Length > 0 ? documentation : null);
    }

    private static CompletionKind KindOf(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            switch (tag)
            {
                case WellKnownTags.Class: return CompletionKind.Class;
                case WellKnownTags.Structure: return CompletionKind.Struct;
                case WellKnownTags.Interface: return CompletionKind.Interface;
                case WellKnownTags.Enum: return CompletionKind.Enum;
                case WellKnownTags.EnumMember: return CompletionKind.EnumMember;
                case WellKnownTags.Delegate: return CompletionKind.Function;
                case WellKnownTags.Method:
                case WellKnownTags.ExtensionMethod: return CompletionKind.Method;
                case WellKnownTags.Property: return CompletionKind.Property;
                case WellKnownTags.Field: return CompletionKind.Field;
                case WellKnownTags.Event: return CompletionKind.Event;
                case WellKnownTags.Constant: return CompletionKind.Constant;
                case WellKnownTags.Local:
                case WellKnownTags.Parameter:
                case WellKnownTags.RangeVariable: return CompletionKind.Variable;
                case WellKnownTags.Namespace:
                case WellKnownTags.Module:
                case WellKnownTags.Assembly: return CompletionKind.Module;
                case WellKnownTags.TypeParameter: return CompletionKind.TypeParameter;
                case WellKnownTags.Operator: return CompletionKind.Operator;
                case WellKnownTags.Keyword:
                case WellKnownTags.Intrinsic: return CompletionKind.Keyword;
                case WellKnownTags.Snippet: return CompletionKind.Snippet;
                case WellKnownTags.File: return CompletionKind.File;
                case WellKnownTags.Folder: return CompletionKind.Folder;
            }
        }

        return CompletionKind.Text;
    }

    // -----------------------------------------------------------------------------------------
    // Hover
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Roslyn's quick info, plus — for a framework member — the JavaScript CSJS emits for it, or a
    /// warning that it has none. That second part is the transpiler-specific half of IntelliSense:
    /// knowing what a member becomes is as useful as knowing its type.
    /// </summary>
    public async Task<HoverResult?> GetHoverAsync(string path, TextPosition position, CancellationToken cancellationToken = default)
    {
        var document = Snapshot(path);
        if (document is null) return null;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var offset = ToOffset(text, position);

        var markdown = new StringBuilder();
        TextSpan? span = null;

        var quickInfo = QuickInfoService.GetService(document);
        if (quickInfo is not null)
        {
            var item = await quickInfo.GetQuickInfoAsync(document, offset, cancellationToken).ConfigureAwait(false);
            if (item is not null)
            {
                span = item.Span;
                foreach (var section in item.Sections)
                {
                    if (string.IsNullOrWhiteSpace(section.Text)) continue;
                    if (markdown.Length > 0) markdown.Append("\n\n");

                    if (section.Kind == QuickInfoSectionKinds.Description)
                        markdown.Append("```csharp\n").Append(section.Text.Trim()).Append("\n```");
                    else
                        markdown.Append(EscapeMarkdown(section.Text.Trim()));
                }
            }
        }

        var symbol = await SymbolAtAsync(document, offset, cancellationToken).ConfigureAwait(false);
        if (symbol is not null && CsjsHover.Describe(symbol) is { } csjs)
        {
            if (markdown.Length > 0) markdown.Append("\n\n---\n\n");
            markdown.Append(csjs);
        }

        if (markdown.Length == 0) return null;

        if (span is null)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            span = root?.FindToken(offset).Span ?? new TextSpan(offset, 0);
        }

        return new HoverResult(markdown.ToString(), ToRange(text, span.Value));
    }

    private static string EscapeMarkdown(string text)
        => text.Replace("<", "\\<", StringComparison.Ordinal).Replace("*", "\\*", StringComparison.Ordinal);

    // -----------------------------------------------------------------------------------------
    // Signature help
    // -----------------------------------------------------------------------------------------

    /// <summary>Overloads of the call the caret is inside, with the active one and active argument.</summary>
    public async Task<SignatureHelpResult?> GetSignatureHelpAsync(string path, TextPosition position, CancellationToken cancellationToken = default)
    {
        var document = Snapshot(path);
        if (document is null) return null;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var offset = ToOffset(text, position);

        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null) return null;

        return SignatureHelpBuilder.Build(model, offset, cancellationToken);
    }

    // -----------------------------------------------------------------------------------------
    // Definition
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Where the symbol at a position is declared, when that is in the project's own source.
    /// Framework symbols have no source here and yield nothing.
    /// </summary>
    public async Task<IReadOnlyList<DefinitionLocation>> GetDefinitionAsync(string path, TextPosition position, CancellationToken cancellationToken = default)
    {
        var document = Snapshot(path);
        if (document is null) return Array.Empty<DefinitionLocation>();

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var symbol = await SymbolAtAsync(document, ToOffset(text, position), cancellationToken).ConfigureAwait(false);
        if (symbol is null) return Array.Empty<DefinitionLocation>();

        if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) symbol = reduced;
        symbol = symbol.OriginalDefinition;

        var results = new List<DefinitionLocation>();
        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || location.SourceTree is null) continue;
            var span = location.GetLineSpan();
            results.Add(new DefinitionLocation(
                location.SourceTree.FilePath,
                new TextRange(
                    new TextPosition(span.StartLinePosition.Line, span.StartLinePosition.Character),
                    new TextPosition(span.EndLinePosition.Line, span.EndLinePosition.Character))));
        }

        return results;
    }

    private static async Task<ISymbol?> SymbolAtAsync(Document document, int offset, CancellationToken cancellationToken)
    {
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset, cancellationToken).ConfigureAwait(false);

        // At the end of an identifier the caret sits just past it — the usual spot after typing.
        if (symbol is null && offset > 0)
            symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset - 1, cancellationToken).ConfigureAwait(false);

        return symbol;
    }

    // -----------------------------------------------------------------------------------------
    // Diagnostics
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Compiler diagnostics plus the transpiler's own, for one document or — with a null path —
    /// for every document. CSJS diagnostics come from running <see cref="Transpiler"/> itself, so
    /// <c>CSJS0002</c> and friends appear inline exactly when the build would report them.
    /// </summary>
    public async Task<IReadOnlyList<LanguageDiagnostic>> GetDiagnosticsAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        Solution solution;
        int version;
        List<Document> documents;

        lock (_gate)
        {
            solution = _workspace.CurrentSolution;
            version = _version;

            var ids = path is null
                ? _documents.Values.ToList()
                : _documents.TryGetValue(Normalize(path), out var one) ? new List<DocumentId> { one } : new List<DocumentId>();

            documents = ids.Select(solution.GetDocument).OfType<Document>().ToList();
        }

        var results = new List<LanguageDiagnostic>();
        if (documents.Count == 0) return results;

        foreach (var document in documents)
        {
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model is null) continue;

            foreach (var diagnostic in model.GetDiagnostics(cancellationToken: cancellationToken))
            {
                if (diagnostic.IsSuppressed || Transpiler.IgnoredRoslynDiagnostics.Contains(diagnostic.Id)) continue;

                var severity = diagnostic.Severity switch
                {
                    RoslynSeverity.Error => LanguageDiagnosticSeverity.Error,
                    RoslynSeverity.Warning => LanguageDiagnosticSeverity.Warning,
                    _ => (LanguageDiagnosticSeverity?)null,
                };
                if (severity is null || !diagnostic.Location.IsInSource) continue;

                var span = diagnostic.Location.GetLineSpan();
                results.Add(new LanguageDiagnostic(
                    document.FilePath ?? document.Name,
                    new TextRange(
                        new TextPosition(span.StartLinePosition.Line, span.StartLinePosition.Character),
                        new TextPosition(span.EndLinePosition.Line, span.EndLinePosition.Character)),
                    severity.Value,
                    diagnostic.Id,
                    diagnostic.GetMessage(),
                    "csharp"));
            }
        }

        if (!_options.IncludeTranspilerDiagnostics) return results;

        var transpiled = await TranspileAsync(solution, version, cancellationToken).ConfigureAwait(false);
        var wanted = new HashSet<string>(documents.Select(d => d.FilePath ?? d.Name), PathComparer);

        foreach (var diagnostic in transpiled)
        {
            // Compiler diagnostics were already taken from the workspace above; the transpiler's
            // copies of them would only duplicate the list.
            if (!diagnostic.Id.StartsWith("CSJS", StringComparison.Ordinal)) continue;
            if (diagnostic.FilePath is null || !wanted.Contains(diagnostic.FilePath)) continue;

            var document = documents.First(d => PathComparer.Equals(d.FilePath ?? d.Name, diagnostic.FilePath));
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

            results.Add(new LanguageDiagnostic(
                diagnostic.FilePath,
                await RangeOfAsync(document, text, diagnostic, cancellationToken).ConfigureAwait(false),
                diagnostic.Severity switch
                {
                    Core.DiagnosticSeverity.Error => LanguageDiagnosticSeverity.Error,
                    Core.DiagnosticSeverity.Warning => LanguageDiagnosticSeverity.Warning,
                    _ => LanguageDiagnosticSeverity.Information,
                },
                diagnostic.Id,
                diagnostic.Message,
                "csjs"));
        }

        return results;
    }

    /// <summary>
    /// Transpiles the whole snapshot once per document version. Diagnostics for several files are
    /// usually asked for together, and one transpile answers all of them.
    /// </summary>
    private Task<IReadOnlyList<CsjsDiagnostic>> TranspileAsync(Solution solution, int version, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_transpile is { } run && run.Version == version) return run.Result.WaitAsync(cancellationToken);

            var sources = solution.GetProject(_projectId)!.Documents
                .Select(d => (Path: d.FilePath ?? d.Name, Document: d))
                .ToList();

            var task = Task.Run(async () =>
            {
                var documents = new List<SourceDocument>(sources.Count);
                foreach (var (path, document) in sources)
                {
                    var text = await document.GetTextAsync(CancellationToken.None).ConfigureAwait(false);
                    documents.Add(new SourceDocument(path, text.ToString()));
                }

                var options = new TranspileOptions
                {
                    EmitSourceMap = false,
                    EmitTypeDefinitions = false,
                    AssemblyName = _options.AssemblyName,
                };
                options.DefineConstants.AddRange(_options.DefineConstants);
                options.AdditionalReferences.AddRange(_options.AdditionalReferences);
                options.AdditionalReferences.Add(typeof(CSJS.Testing.Assert).Assembly.Location);
                options.AdditionalReferences.Add(typeof(CSJS.Web.Dom).Assembly.Location);

                try
                {
                    return (IReadOnlyList<CsjsDiagnostic>)new Transpiler(options).Transpile(documents).Diagnostics;
                }
                catch (Exception)
                {
                    // Half-typed code can reach corners the emitter never expected. Live
                    // diagnostics are advisory; the build reports whatever went wrong properly.
                    return Array.Empty<CsjsDiagnostic>();
                }
            }, CancellationToken.None);

            _transpile = new TranspileRun(version, task);
            return task.WaitAsync(cancellationToken);
        }
    }

    private static async Task<TextRange> RangeOfAsync(Document document, SourceText text, CsjsDiagnostic diagnostic, CancellationToken cancellationToken)
    {
        var start = new TextPosition(Math.Max(0, diagnostic.Line - 1), Math.Max(0, diagnostic.Column - 1));

        if (diagnostic.EndLine > 0)
            return new TextRange(start, new TextPosition(diagnostic.EndLine - 1, Math.Max(0, diagnostic.EndColumn - 1)));

        // Only a start is known: underline the token there rather than guessing a width.
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return new TextRange(start, start);

        var token = root.FindToken(ToOffset(text, start));
        return ToRange(text, token.Span);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private Document? Snapshot(string path)
    {
        lock (_gate)
        {
            return _documents.TryGetValue(Normalize(path), out var id)
                ? _workspace.CurrentSolution.GetDocument(id)
                : null;
        }
    }

    private static string Normalize(string path) => Path.GetFullPath(path);

    internal static int ToOffset(SourceText text, TextPosition position)
    {
        if (text.Lines.Count == 0) return 0;

        var line = Math.Clamp(position.Line, 0, text.Lines.Count - 1);
        var info = text.Lines[line];
        return info.Start + Math.Clamp(position.Character, 0, info.End - info.Start);
    }

    internal static TextRange ToRange(SourceText text, TextSpan span)
    {
        var lines = text.Lines.GetLinePositionSpan(span);
        return new TextRange(
            new TextPosition(lines.Start.Line, lines.Start.Character),
            new TextPosition(lines.End.Line, lines.End.Character));
    }

    public void Dispose() => _workspace.Dispose();

    private sealed record CachedCompletion(int Id, Document Document, CompletionList List);

    private sealed record TranspileRun(int Version, Task<IReadOnlyList<CsjsDiagnostic>> Result);
}
