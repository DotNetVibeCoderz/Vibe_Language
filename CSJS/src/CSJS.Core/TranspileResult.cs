namespace CSJS.Core;

/// <summary>Severity of a transpiler message.</summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A message produced while parsing, binding, or emitting.</summary>
/// <param name="Id">Stable identifier, e.g. <c>CSJS0101</c> or a Roslyn id like <c>CS0103</c>.</param>
/// <param name="Severity">How bad it is.</param>
/// <param name="Message">Human-readable text.</param>
/// <param name="FilePath">Source file, when known.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Column">1-based column.</param>
/// <param name="EndLine">1-based line where the reported span ends, or 0 when only the start is known.</param>
/// <param name="EndColumn">1-based column (exclusive) where the reported span ends, or 0 when unknown.</param>
public sealed record CsjsDiagnostic(
    string Id,
    DiagnosticSeverity Severity,
    string Message,
    string? FilePath = null,
    int Line = 0,
    int Column = 0,
    int EndLine = 0,
    int EndColumn = 0)
{
    public override string ToString()
    {
        var where = FilePath is null ? "" : $"{FilePath}({Line},{Column}): ";
        var sev = Severity.ToString().ToLowerInvariant();
        return $"{where}{sev} {Id}: {Message}";
    }
}

/// <summary>One emitted JavaScript module and its companion artifacts.</summary>
/// <param name="FileName">Output file name, e.g. <c>Program.js</c>.</param>
/// <param name="JavaScript">The emitted JavaScript.</param>
/// <param name="SourceMap">Source map JSON, or null when disabled.</param>
/// <param name="TypeDefinitions"><c>.d.ts</c> text, or null when disabled.</param>
/// <param name="SourcePath">Originating C# file.</param>
public sealed record EmittedModule(
    string FileName,
    string JavaScript,
    string? SourceMap,
    string? TypeDefinitions,
    string SourcePath);

/// <summary>Outcome of a transpilation run.</summary>
public sealed class TranspileResult
{
    public required IReadOnlyList<EmittedModule> Modules { get; init; }
    public required IReadOnlyList<CsjsDiagnostic> Diagnostics { get; init; }

    /// <summary>True when nothing with <see cref="DiagnosticSeverity.Error"/> was reported.</summary>
    public bool Success => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public IEnumerable<CsjsDiagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
    public IEnumerable<CsjsDiagnostic> Warnings => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning);

    /// <summary>The runtime helper module source, emitted when any module imported a helper.</summary>
    public string? RuntimeJavaScript { get; init; }

    public static TranspileResult Failed(params CsjsDiagnostic[] diagnostics) => new()
    {
        Modules = Array.Empty<EmittedModule>(),
        Diagnostics = diagnostics,
    };
}

/// <summary>A C# input document.</summary>
/// <param name="Path">Path used for diagnostics, source maps, and the output file name.</param>
/// <param name="Text">C# source text.</param>
public sealed record SourceDocument(string Path, string Text);
