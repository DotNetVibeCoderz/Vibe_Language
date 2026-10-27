namespace CSJS.LanguageService;

// The service speaks in editor coordinates rather than Roslyn's: zero-based lines and UTF-16
// columns, which is what LSP uses natively. Monaco is one-based, and its adapter adds the one.

/// <summary>A zero-based line and UTF-16 column.</summary>
public readonly record struct TextPosition(int Line, int Character);

/// <summary>A half-open range between two positions.</summary>
public readonly record struct TextRange(TextPosition Start, TextPosition End);

/// <summary>
/// Completion item kinds. The numeric values are LSP's <c>CompletionItemKind</c>, so the server
/// sends them as they are; Monaco's enum differs and is mapped by name on the JavaScript side.
/// </summary>
public enum CompletionKind
{
    Text = 1,
    Method = 2,
    Function = 3,
    Constructor = 4,
    Field = 5,
    Variable = 6,
    Class = 7,
    Interface = 8,
    Module = 9,
    Property = 10,
    Unit = 11,
    Value = 12,
    Enum = 13,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    File = 17,
    Reference = 18,
    Folder = 19,
    EnumMember = 20,
    Constant = 21,
    Struct = 22,
    Event = 23,
    Operator = 24,
    TypeParameter = 25,
}

/// <summary>One completion candidate.</summary>
/// <param name="Label">What the list shows, e.g. <c>List&lt;&gt;</c>.</param>
/// <param name="InsertText">What accepting it writes.</param>
/// <param name="FilterText">Text the client matches the typed prefix against.</param>
/// <param name="SortText">Ordering key.</param>
/// <param name="Detail">A short inline description, when Roslyn has one.</param>
/// <param name="Kind">Icon and category.</param>
/// <param name="ReplaceRange">The span accepting the item replaces — the identifier being typed.</param>
/// <param name="Index">Position in the result, used to resolve details lazily.</param>
/// <param name="Preselect">True for the item Roslyn would select by default.</param>
public sealed record CompletionEntry(
    string Label,
    string InsertText,
    string? FilterText,
    string? SortText,
    string? Detail,
    CompletionKind Kind,
    TextRange ReplaceRange,
    int Index,
    bool Preselect);

/// <summary>A completion list. <paramref name="ResultId"/> identifies it for lazy resolution.</summary>
public sealed record CompletionResult(int ResultId, IReadOnlyList<CompletionEntry> Items);

/// <summary>Details computed only for the item the user is looking at.</summary>
/// <param name="Detail">The signature line.</param>
/// <param name="Documentation">Markdown documentation, when any exists.</param>
public sealed record CompletionDetails(string? Detail, string? Documentation);

/// <summary>Quick info, as Markdown, and the span it describes.</summary>
public sealed record HoverResult(string Markdown, TextRange Range);

/// <summary>A parameter inside a signature label, located by UTF-16 offsets into that label.</summary>
public sealed record SignatureParameter(string Label, int Start, int End, string? Documentation);

/// <summary>One overload.</summary>
public sealed record SignatureEntry(string Label, string? Documentation, IReadOnlyList<SignatureParameter> Parameters);

/// <summary>Signature help at a call site.</summary>
public sealed record SignatureHelpResult(IReadOnlyList<SignatureEntry> Signatures, int ActiveSignature, int ActiveParameter);

/// <summary>Where a symbol is declared.</summary>
public sealed record DefinitionLocation(string Path, TextRange Range);

/// <summary>Severity, numbered as LSP's <c>DiagnosticSeverity</c>.</summary>
public enum LanguageDiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4,
}

/// <summary>A problem in a document.</summary>
/// <param name="Path">The document's full path.</param>
/// <param name="Range">What to underline.</param>
/// <param name="Severity">How bad it is.</param>
/// <param name="Code">The diagnostic id, e.g. <c>CS0103</c> or <c>CSJS0002</c>.</param>
/// <param name="Message">Human-readable text.</param>
/// <param name="Source"><c>csharp</c> for compiler diagnostics, <c>csjs</c> for the transpiler's own.</param>
public sealed record LanguageDiagnostic(
    string Path,
    TextRange Range,
    LanguageDiagnosticSeverity Severity,
    string Code,
    string Message,
    string Source);

/// <summary>How the service compiles the documents it holds. Mirrors <c>TranspileOptions</c>.</summary>
public sealed class LanguageServiceOptions
{
    /// <summary>Preprocessor symbols. <c>CSJS</c> is always defined, as it is for a build.</summary>
    public List<string> DefineConstants { get; } = new();

    /// <summary>Extra assembly paths, typically <c>references</c> from <c>csjs.json</c>.</summary>
    public List<string> AdditionalReferences { get; } = new();

    /// <summary>Assembly name for the underlying compilation.</summary>
    public string AssemblyName { get; set; } = "CsjsModule";

    /// <summary>
    /// Run the transpiler too, so CSJS diagnostics such as <c>CSJS0002</c> appear inline. This is
    /// what makes the editor agree with the build; turning it off leaves plain C# diagnostics.
    /// </summary>
    public bool IncludeTranspilerDiagnostics { get; set; } = true;
}
