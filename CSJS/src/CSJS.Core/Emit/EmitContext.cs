using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSJS.Core.Emit;

/// <summary>A JavaScript module import the emitted file needs.</summary>
/// <param name="Module">Module specifier.</param>
/// <param name="Export">Named export, or null for a default import.</param>
/// <param name="Alias">Local binding name.</param>
public readonly record struct JsImport(string Module, string? Export, string Alias);

/// <summary>
/// State shared by every part of the emitter while it walks one source file: the semantic model
/// it resolves symbols against, the writer it produces text on, and the imports it accumulates.
/// </summary>
public sealed class EmitContext
{
    public required SemanticModel Model { get; init; }
    public required JsWriter Writer { get; init; }
    public required TranspileOptions Options { get; init; }
    public required NameResolver Names { get; init; }
    public required List<CsjsDiagnostic> Diagnostics { get; init; }
    public required string FilePath { get; init; }

    /// <summary>Maps a declared type onto the module that owns it, so cross-file uses can import it.</summary>
    public required IReadOnlyDictionary<INamedTypeSymbol, string> TypeModules { get; init; }

    /// <summary>Module specifier of the file currently being emitted.</summary>
    public required string CurrentModule { get; init; }

    /// <summary>Whole-program facts: unused private members, enums that can be inlined.</summary>
    public ProgramAnalysis Analysis { get; init; } = ProgramAnalysis.Empty;

    private readonly SortedSet<string> _helpers = new(StringComparer.Ordinal);
    private readonly HashSet<JsImport> _imports = new();
    private int _temporaryCounter;

    /// <summary>Runtime helpers referenced so far, in a stable order.</summary>
    public IReadOnlyCollection<string> Helpers => _helpers;

    /// <summary>JavaScript module imports collected so far.</summary>
    public IReadOnlyCollection<JsImport> Imports => _imports;

    /// <summary>Records that a runtime helper is needed, so the import block can include it.</summary>
    public void UseHelper(string name) => _helpers.Add(name);

    /// <summary>Records a module import. Duplicates collapse.</summary>
    public void UseImport(string module, string? export, string alias)
        => _imports.Add(new JsImport(module, export, alias));

    /// <summary>Generates a collision-free temporary identifier.</summary>
    public string NextTemporary(string hint = "t") => $"${hint}{_temporaryCounter++}";

    public void Error(string id, string message, SyntaxNode node)
        => Report(id, DiagnosticSeverity.Error, message, node);

    public void Warn(string id, string message, SyntaxNode node)
        => Report(id, DiagnosticSeverity.Warning, message, node);

    private void Report(string id, DiagnosticSeverity severity, string message, SyntaxNode node)
    {
        // The end is carried too, so an editor can underline the whole construct rather than
        // guessing a width from the start column.
        var span = node.GetLocation().GetLineSpan();
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;
        Diagnostics.Add(new CsjsDiagnostic(
            id, severity, message, FilePath,
            start.Line + 1, start.Character + 1,
            end.Line + 1, end.Character + 1));
    }

    /// <summary>
    /// Synthetic expressions standing in for a real one. A rewritten <c>a?.M()</c> binds its
    /// receiver to a parameter; the stand-in is what the emitter passes around as the receiver,
    /// and semantic questions about it are answered for the expression it replaced.
    /// </summary>
    private readonly Dictionary<ExpressionSyntax, ExpressionSyntax> _standIns = new();

    /// <summary>The emitted name of each stand-in.</summary>
    private readonly Dictionary<ExpressionSyntax, string> _standInNames = new();

    public ExpressionSyntax CreateStandIn(ExpressionSyntax original, string name)
    {
        var standIn = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName(name);
        _standIns[standIn] = original;
        _standInNames[standIn] = name;
        return standIn;
    }

    /// <summary>The JavaScript name a stand-in is written as, or null for a real expression.</summary>
    public string? StandInName(SyntaxNode node) => node is ExpressionSyntax e && _standInNames.TryGetValue(e, out var n) ? n : null;

    private SyntaxNode Real(SyntaxNode node) => node is ExpressionSyntax e && _standIns.TryGetValue(e, out var original) ? original : node;

    /// <summary>Resolves a symbol for a node, or null when the model could not bind it.</summary>
    public ISymbol? SymbolFor(SyntaxNode node)
    {
        node = Real(node);
        return Model.GetSymbolInfo(node).Symbol ?? Model.GetSymbolInfo(node).CandidateSymbols.FirstOrDefault();
    }

    public ITypeSymbol? TypeFor(ExpressionSyntax node) => Model.GetTypeInfo((ExpressionSyntax)Real(node)).Type;

    public ITypeSymbol? ConvertedTypeFor(ExpressionSyntax node) => Model.GetTypeInfo((ExpressionSyntax)Real(node)).ConvertedType;
}

/// <summary>
/// Paths between emitted modules. Output mirrors the source tree, so a module in
/// <c>Models/</c> imports one at the root as <c>../Shared.js</c> — writing <c>./Shared.js</c>
/// from everywhere only worked while every source file sat in one folder.
/// </summary>
public static class ModulePaths
{
    /// <summary>Forward slashes, no leading <c>./</c>.</summary>
    public static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        return normalized;
    }

    /// <summary>
    /// The ES module specifier <paramref name="from"/> uses to import <paramref name="to"/>, both
    /// given relative to the output root.
    /// </summary>
    public static string Relative(string from, string to)
    {
        var fromParts = Normalize(from).Split('/');
        var toParts = Normalize(to).Split('/');

        // Directories only: the last segment of `from` is the importing file itself.
        var common = 0;
        while (common < fromParts.Length - 1 && common < toParts.Length - 1
               && string.Equals(fromParts[common], toParts[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        var ups = fromParts.Length - 1 - common;
        var rest = string.Join("/", toParts.Skip(common));
        return ups == 0 ? "./" + rest : string.Concat(Enumerable.Repeat("../", ups)) + rest;
    }
}

/// <summary>Well-known CSJS diagnostic identifiers.</summary>
public static class DiagnosticIds
{
    public const string UnsupportedSyntax = "CSJS0001";
    public const string UnsupportedMember = "CSJS0002";
    public const string UnresolvedSymbol = "CSJS0003";
    public const string UnsupportedPattern = "CSJS0004";
    public const string PointerUnsupported = "CSJS0005";
    public const string LockIgnored = "CSJS0006";
    public const string CompilationError = "CSJS0100";
    public const string NoOutput = "CSJS0101";
}
