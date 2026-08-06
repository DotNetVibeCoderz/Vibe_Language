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
        var position = node.GetLocation().GetLineSpan().StartLinePosition;
        Diagnostics.Add(new CsjsDiagnostic(id, severity, message, FilePath, position.Line + 1, position.Character + 1));
    }

    /// <summary>Resolves a symbol for a node, or null when the model could not bind it.</summary>
    public ISymbol? SymbolFor(SyntaxNode node) => Model.GetSymbolInfo(node).Symbol
        ?? Model.GetSymbolInfo(node).CandidateSymbols.FirstOrDefault();

    public ITypeSymbol? TypeFor(ExpressionSyntax node) => Model.GetTypeInfo(node).Type;

    public ITypeSymbol? ConvertedTypeFor(ExpressionSyntax node) => Model.GetTypeInfo(node).ConvertedType;
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
