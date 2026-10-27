using CSJS.LanguageService;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// The language service behind <c>cs2js lsp</c> and CSJSGen's editor. These drive it the way an
/// editor does — a document, a caret position, a question — and check the answer is the real
/// C# one, with CSJS's own diagnostics folded in.
/// </summary>
public sealed class LanguageServiceTests : IDisposable
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "csjs-ls-tests");
    private static readonly string ProgramPath = Path.Combine(Root, "Program.csjs");
    private static readonly string ModelsPath = Path.Combine(Root, "Models.cs");

    private readonly CsjsLanguageService _service = new();

    public void Dispose() => _service.Dispose();

    /// <summary>Loads <paramref name="source"/> with a <c>$$</c> caret marker and returns the caret.</summary>
    private TextPosition Load(string path, string source)
    {
        var offset = source.IndexOf("$$", StringComparison.Ordinal);
        var text = offset < 0 ? source : source.Remove(offset, 2);
        _service.UpdateDocument(path, text);
        return offset < 0 ? default : PositionOf(text, offset);
    }

    private static TextPosition PositionOf(string text, int offset)
    {
        var before = text[..offset];
        var line = before.Count(c => c == '\n');
        var column = offset - (before.LastIndexOf('\n') + 1);
        return new TextPosition(line, column);
    }

    [Fact]
    public async Task CompletesMembersOfABclType()
    {
        var caret = Load(ProgramPath, """
            public static class App
            {
                public static void Main()
                {
                    var name = "csjs";
                    var upper = name.$$
                }
            }
            """);

        var result = await _service.GetCompletionsAsync(ProgramPath, caret);

        Assert.NotNull(result);
        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("Substring", labels);
        Assert.Contains("ToUpperInvariant", labels);
        Assert.Contains("Length", labels);
        Assert.Equal(CompletionKind.Method, result.Items.First(i => i.Label == "Substring").Kind);
        Assert.Equal(CompletionKind.Property, result.Items.First(i => i.Label == "Length").Kind);
    }

    [Fact]
    public async Task CompletesTheBrowserBindingsFromCsjsInterop()
    {
        var caret = Load(ProgramPath, """
            using CSJS.Browser;

            public static class App
            {
                public static void Start()
                {
                    var heading = Document.$$
                }
            }
            """);

        var result = await _service.GetCompletionsAsync(ProgramPath, caret);

        Assert.NotNull(result);
        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("QuerySelector", labels);
        Assert.Contains("GetElementById", labels);
        Assert.Contains("Body", labels);
    }

    [Fact]
    public async Task ResolvesCompletionDetailsLazily()
    {
        var caret = Load(ProgramPath, """
            public static class App
            {
                public static void Main()
                {
                    var name = "csjs";
                    name.$$
                }
            }
            """);

        var result = await _service.GetCompletionsAsync(ProgramPath, caret);
        Assert.NotNull(result);

        var substring = result.Items.First(i => i.Label == "Substring");
        var details = await _service.ResolveCompletionAsync(result.ResultId, substring.Index);

        Assert.NotNull(details);
        Assert.Contains("Substring(int startIndex", details.Detail);
    }

    [Fact]
    public async Task HoverShowsTheCSharpSignatureAndTheJavaScriptItEmits()
    {
        var caret = Load(ProgramPath, """
            public static class App
            {
                public static string Shout(string text) => text.ToUpp$$erInvariant();
            }
            """);

        var hover = await _service.GetHoverAsync(ProgramPath, caret);

        Assert.NotNull(hover);
        Assert.Contains("```csharp", hover.Markdown);
        Assert.Contains("ToUpperInvariant()", hover.Markdown);
        // The CSJS half comes from MemberMap, the same table the emitter reads.
        Assert.Contains("{this}.toUpperCase()", hover.Markdown);
    }

    [Fact]
    public async Task HoverOnABindingShowsTheEmittedJavaScriptName()
    {
        var caret = Load(ProgramPath, """
            using CSJS.Browser;

            public static class App
            {
                public static void Start() => Document.QuerySel$$ector("h1");
            }
            """);

        var hover = await _service.GetHoverAsync(ProgramPath, caret);

        Assert.NotNull(hover);
        Assert.Contains("document.querySelector", hover.Markdown);
    }

    [Fact]
    public async Task UnmappedFrameworkMemberIsReportedInlineAsCsjs0002()
    {
        Load(ProgramPath, """
            using System;

            public static class App
            {
                public static void Main()
                {
                    Console.Beep();
                }
            }
            """);

        var diagnostics = await _service.GetDiagnosticsAsync(ProgramPath);

        var unmapped = Assert.Single(diagnostics, d => d.Code == "CSJS0002");
        Assert.Equal(LanguageDiagnosticSeverity.Error, unmapped.Severity);
        Assert.Equal("csjs", unmapped.Source);
        Assert.Equal(Path.GetFullPath(ProgramPath), unmapped.Path, ignoreCase: true);

        // The range covers the call on line 7, not just a guessed width from its start.
        Assert.Equal(6, unmapped.Range.Start.Line);
        Assert.Equal(8, unmapped.Range.Start.Character);
        Assert.True(unmapped.Range.End.Character > unmapped.Range.Start.Character + "Console".Length);
    }

    [Fact]
    public async Task CompilerErrorsAreReportedWithTheirRange()
    {
        Load(ProgramPath, """
            public static class App
            {
                public static int Main() => missing + 1;
            }
            """);

        var diagnostics = await _service.GetDiagnosticsAsync(ProgramPath);

        var error = Assert.Single(diagnostics, d => d.Code == "CS0103");
        Assert.Equal("csharp", error.Source);
        Assert.Equal(new TextPosition(2, 32), error.Range.Start);
        Assert.Equal(new TextPosition(2, 39), error.Range.End);
    }

    [Fact]
    public async Task CleanCodeHasNoDiagnostics()
    {
        Load(ProgramPath, """
            using System;
            using System.Collections.Generic;

            public static class App
            {
                public static void Main()
                {
                    var items = new List<int> { 3, 1, 2 };
                    items.Sort();
                    Console.WriteLine(items.Count);
                }
            }
            """);

        var diagnostics = await _service.GetDiagnosticsAsync(ProgramPath);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task GoesToADefinitionInAnotherProjectFile()
    {
        Load(ModelsPath, """
            public sealed class Greeter
            {
                public string Greet(string name) => "Hello, " + name;
            }
            """);

        var caret = Load(ProgramPath, """
            public static class App
            {
                public static string Main() => new Greeter().Gr$$eet("csjs");
            }
            """);

        var definitions = await _service.GetDefinitionAsync(ProgramPath, caret);

        var definition = Assert.Single(definitions);
        Assert.Equal(Path.GetFullPath(ModelsPath), definition.Path, ignoreCase: true);
        Assert.Equal(new TextPosition(2, 18), definition.Range.Start);
    }

    [Fact]
    public async Task SignatureHelpTracksTheActiveArgument()
    {
        var caret = Load(ProgramPath, """
            public static class App
            {
                public static string Main() => "csjs".Substring(1, $$);
            }
            """);

        var help = await _service.GetSignatureHelpAsync(ProgramPath, caret);

        Assert.NotNull(help);
        Assert.Equal(1, help.ActiveParameter);
        Assert.True(help.Signatures.Count >= 2, "Both Substring overloads should be offered.");

        var active = help.Signatures[help.ActiveSignature];
        Assert.Equal(2, active.Parameters.Count);
        Assert.Equal("int length", active.Label[active.Parameters[1].Start..active.Parameters[1].End]);
    }

    [Fact]
    public async Task CsjsDefineIsVisibleToTheEditorAsItIsToTheBuild()
    {
        Load(ProgramPath, """
            public static class App
            {
            #if CSJS
                public static int Main() => 1;
            #else
                public static int Main() => missing;
            #endif
            }
            """);

        var diagnostics = await _service.GetDiagnosticsAsync(ProgramPath);

        Assert.DoesNotContain(diagnostics, d => d.Code == "CS0103");
    }

    [Fact]
    public async Task RemovedDocumentsStopContributing()
    {
        Load(ModelsPath, "public sealed class Greeter { }");
        Load(ProgramPath, "public static class App { public static object Make() => new Greeter(); }");

        Assert.Empty(await _service.GetDiagnosticsAsync(ProgramPath));

        _service.RemoveDocument(ModelsPath);

        var diagnostics = await _service.GetDiagnosticsAsync(ProgramPath);
        Assert.Contains(diagnostics, d => d.Code == "CS0246");
    }
}
