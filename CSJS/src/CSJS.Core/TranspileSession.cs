using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using CSJS.Core.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSJS.Core;

/// <summary>
/// Callbacks a <see cref="Transpiler"/> consults so a <see cref="TranspileSession"/> can reuse
/// work from a previous run. Every hook is optional and a transpiler without them behaves exactly
/// as before, which keeps the session a layer over the one pipeline rather than a second one.
/// </summary>
internal sealed class TranspileHooks
{
    /// <summary>Metadata references to bind against, instead of loading them again.</summary>
    public ImmutableArray<MetadataReference>? References { get; init; }

    /// <summary>
    /// Given the freshly created compilation and its trees, returns the compilation to use. It may
    /// swap entries of the tree list for the identical trees of a previous compilation.
    /// </summary>
    public Func<CSharpCompilation, List<SyntaxTree>, CSharpCompilation>? Compilation { get; init; }

    /// <summary>
    /// Returns a previously emitted module for document <c>i</c> when it is provably unchanged,
    /// adding that module's emitter diagnostics to the list; null to emit it afresh.
    /// </summary>
    public Func<int, ProgramAnalysis, List<CsjsDiagnostic>, EmittedModule?>? ReuseModule { get; init; }
}

/// <summary>What the last <see cref="TranspileSession.Transpile"/> call reused.</summary>
/// <param name="Emitted">Modules emitted afresh.</param>
/// <param name="Reused">Modules carried over unchanged from an earlier run.</param>
/// <param name="ReferencesReused">Whether the framework reference set came from the cache.</param>
/// <param name="CompilationReused">Whether the previous compilation was updated rather than rebuilt.</param>
/// <param name="SurfaceChanged">Whether the declared surface changed, forcing every module to re-emit.</param>
public sealed record TranspileSessionStats(int Emitted, int Reused, bool ReferencesReused, bool CompilationReused, bool SurfaceChanged);

/// <summary>
/// A transpiler that remembers. Watch mode and the dev server keep one alive across rebuilds so
/// an edit costs what it changes rather than a whole cold build.
/// <para>
/// Three things carry over. The <b>metadata references</b> — the framework assemblies, the
/// largest fixed cost of a build — are loaded once. The <b>compilation</b> is updated with
/// <c>ReplaceSyntaxTree</c> for the files that changed, so Roslyn reuses its declaration tables
/// and reference binding. And <b>emitted modules</b> are reused for files that did not change.
/// </para>
/// <para>
/// The last of these is where correctness lives, because a file's JavaScript depends on more
/// than its own text: on the names the <see cref="NameResolver"/> gives other files' types and
/// members, on which module declares what, and on the whole-program <see cref="ProgramAnalysis"/>.
/// A module is reused only when its own text is identical <i>and</i> the declared surface of the
/// whole program — every declaration with method and accessor bodies blanked out — is identical
/// <i>and</i> the analysis facts that apply to it are identical. Anything else re-emits. The
/// surface is deliberately generous: field initialisers, attributes, usings, and every partial
/// type in full all count, so the cost of being wrong is a slower rebuild, never stale output.
/// </para>
/// <para>
/// Diagnostics are always computed over the whole compilation; only emission is skipped. Options
/// are fixed for the life of a session.
/// </para>
/// </summary>
public sealed class TranspileSession
{
    private readonly TranspileOptions _options;

    private ImmutableArray<MetadataReference>? _references;
    private string? _referenceKey;

    private CSharpCompilation? _compilation;
    private List<SyntaxTree> _trees = new();
    private List<SourceDocument> _documents = new();
    private string? _surface;

    private readonly Dictionary<string, CachedModule> _modules = new(StringComparer.Ordinal);

    private sealed record CachedModule(
        string Text, string Surface, string AnalysisKey, EmittedModule Module, IReadOnlyList<CsjsDiagnostic> Diagnostics);

    public TranspileSession(TranspileOptions? options = null) => _options = options ?? new TranspileOptions();

    public TranspileOptions Options => _options;

    /// <summary>What the most recent run reused. Null before the first run.</summary>
    public TranspileSessionStats? LastRun { get; private set; }

    /// <summary>Transpiles, reusing whatever an earlier run proves still valid.</summary>
    public TranspileResult Transpile(IReadOnlyList<SourceDocument> documents)
    {
        var referencesReused = EnsureReferences();
        var compilationReused = false;
        string? surface = null;
        var previousSurface = _surface;
        var reused = new HashSet<string>(StringComparer.Ordinal);
        ProgramAnalysis? analysis = null;
        string? inlinedKey = null;

        string AnalysisKeyFor(string path)
        {
            inlinedKey ??= string.Join("|", analysis!.InlinedEnums.Select(Display).Order(StringComparer.Ordinal));
            var unused = analysis!.UnusedPrivateMembers
                .Where(s => s.DeclaringSyntaxReferences.Any(r => r.SyntaxTree.FilePath == path))
                .Select(Display)
                .Order(StringComparer.Ordinal);
            return inlinedKey + "#" + string.Join("|", unused);
        }

        var hooks = new TranspileHooks
        {
            References = _references,

            Compilation = (fresh, trees) =>
            {
                surface = SurfaceKey(trees);

                var compilation = fresh;
                if (_compilation is not null && SamePaths(_documents, documents))
                {
                    compilation = _compilation;
                    for (var i = 0; i < trees.Count; i++)
                    {
                        if (_documents[i].Text == documents[i].Text)
                            trees[i] = _trees[i];
                        else
                            compilation = compilation.ReplaceSyntaxTree(_trees[i], trees[i]);
                    }
                    compilationReused = true;
                }

                _compilation = compilation;
                _trees = trees.ToList();
                _documents = documents.ToList();
                return compilation;
            },

            ReuseModule = (i, programAnalysis, diagnostics) =>
            {
                analysis = programAnalysis;
                var document = documents[i];
                if (!_modules.TryGetValue(document.Path, out var cached)) return null;
                if (cached.Text != document.Text || cached.Surface != surface) return null;
                if (cached.AnalysisKey != AnalysisKeyFor(document.Path)) return null;

                diagnostics.AddRange(cached.Diagnostics);
                reused.Add(document.Path);
                return cached.Module;
            },
        };

        var result = new Transpiler(_options) { Hooks = hooks }.Transpile(documents);

        // Remember what was emitted this time. A failed compile emits nothing and caches nothing,
        // so the entries from the last good build stay valid for when the error is fixed.
        if (surface is not null && analysis is not null)
        {
            foreach (var module in result.Modules)
            {
                if (reused.Contains(module.SourcePath)) continue;
                var document = documents.FirstOrDefault(d => d.Path == module.SourcePath);
                if (document is null) continue;

                var emitterDiagnostics = result.Diagnostics
                    .Where(d => d.FilePath == document.Path && d.Id.StartsWith("CSJS", StringComparison.Ordinal))
                    .ToList();
                _modules[document.Path] = new CachedModule(
                    document.Text, surface, AnalysisKeyFor(document.Path), module, emitterDiagnostics);
            }

            var current = new HashSet<string>(documents.Select(d => d.Path), StringComparer.Ordinal);
            foreach (var stale in _modules.Keys.Where(k => !current.Contains(k)).ToList()) _modules.Remove(stale);
        }

        if (surface is not null) _surface = surface;

        LastRun = new TranspileSessionStats(
            result.Modules.Count - reused.Count,
            reused.Count,
            referencesReused,
            compilationReused,
            previousSurface is not null && surface is not null && previousSurface != surface);

        return result;
    }

    /// <summary>Loads the reference set unless the cached one is still current.</summary>
    private bool EnsureReferences()
    {
        // Extra references are files a user may rebuild while a session is alive, so their
        // timestamps are part of the key. The framework set is fixed for the process.
        var key = new StringBuilder();
        foreach (var path in _options.AdditionalReferences)
        {
            var info = new FileInfo(path);
            key.Append(path).Append('|').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)
               .Append('|').Append(info.Exists ? info.Length : 0).Append(';');
        }
        foreach (var image in _options.ReferenceImages) key.Append(image.Length).Append(';');

        if (_references is not null && _referenceKey == key.ToString()) return true;

        _references = new Transpiler(_options).BuildReferences();
        _referenceKey = key.ToString();
        _compilation = null; // a compilation is tied to the references it was created with
        return false;
    }

    private static bool SamePaths(List<SourceDocument> previous, IReadOnlyList<SourceDocument> current)
    {
        if (previous.Count != current.Count) return false;
        for (var i = 0; i < previous.Count; i++)
            if (previous[i].Path != current[i].Path) return false;
        return true;
    }

    private static string Display(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        + "(" + string.Join(",", (symbol as IMethodSymbol)?.Parameters.Select(p => p.Type.ToDisplayString()) ?? []) + ")";

    // -----------------------------------------------------------------------------------------
    // Declared surface
    // -----------------------------------------------------------------------------------------

    /// <summary>A hash of every file's path and declared surface, in order.</summary>
    internal static string SurfaceKey(IReadOnlyList<SyntaxTree> trees)
    {
        var sb = new StringBuilder();
        foreach (var tree in trees)
        {
            sb.Append(tree.FilePath).Append('\0');
            AppendSurface(tree.GetRoot(), sb, inPartial: false);
            sb.Append('\0');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>
    /// The declared surface of a file: its tokens, without trivia, with member bodies replaced by a
    /// marker. Whether a body exists is kept (an auto-property and a computed one emit differently),
    /// what it says is not. Top-level statements are reduced to their existence. Partial types are
    /// kept whole, because their parts are emitted from different files.
    /// </summary>
    public static string Surface(SyntaxTree tree)
    {
        var sb = new StringBuilder();
        AppendSurface(tree.GetRoot(), sb, inPartial: false);
        return sb.ToString();
    }

    private static void AppendSurface(SyntaxNode node, StringBuilder sb, bool inPartial)
    {
        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.IsToken)
            {
                sb.Append(child.AsToken().Text).Append(' ');
                continue;
            }

            var n = child.AsNode()!;
            if (n is GlobalStatementSyntax)
            {
                sb.Append("<statement> ");
                continue;
            }

            var partial = inPartial
                || n is TypeDeclarationSyntax type && type.Modifiers.Any(SyntaxKind.PartialKeyword);

            if (!partial && IsMemberBody(n))
            {
                sb.Append("<body> ");
                continue;
            }

            AppendSurface(n, sb, partial);
        }
    }

    private static bool IsMemberBody(SyntaxNode node) => node.Parent switch
    {
        BaseMethodDeclarationSyntax method => node == method.Body || node == method.ExpressionBody,
        AccessorDeclarationSyntax accessor => node == accessor.Body || node == accessor.ExpressionBody,
        BasePropertyDeclarationSyntax => node is ArrowExpressionClauseSyntax,
        _ => false,
    };
}
