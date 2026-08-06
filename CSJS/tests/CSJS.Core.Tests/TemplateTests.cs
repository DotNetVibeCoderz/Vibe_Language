using System.Text.RegularExpressions;
using CSJS.Core;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// Every project template CSJSGen can scaffold has to transpile.
/// <para>
/// The templates live in CSJSGen, which is a Windows desktop project this test assembly does not
/// reference. Rather than take that dependency, the sources are read out of the template file
/// directly — which also means a template that stops compiling fails the build here rather than
/// surfacing the first time somebody creates a project from it.
/// </para>
/// </summary>
public class TemplateTests
{
    /// <summary>
    /// Templates that reference a companion file, and so are only meaningful compiled together.
    /// <see cref="MultiFileTemplatesTranspileTogether"/> covers these instead.
    /// </summary>
    private static readonly HashSet<string> NeedsCompanion = new(StringComparer.Ordinal)
    {
        "CanvasAnimation",  // needs CanvasBindings
        "ThreeScene",       // needs ThreeBindings
        "TestedTests",      // needs TestedLogic
    };

    public static TheoryData<string, string> Templates()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, source) in ReadTemplateSources())
        {
            if (NeedsCompanion.Contains(name)) continue;
            data.Add(name, source);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void TemplateTranspiles(string name, string source)
    {
        var options = new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = true };
        var result = new Transpiler(options).TranspileSource(source, $"{name}.csjs");

        Assert.True(result.Success,
            $"Template '{name}' does not transpile:\n  " +
            string.Join("\n  ", result.Errors.Select(e => e.ToString())));

        Assert.NotEmpty(result.Modules[0].JavaScript);
    }

    /// <summary>
    /// Templates that need more than one file are transpiled together, since a binding file is
    /// only meaningful alongside the program that uses it.
    /// </summary>
    [Fact]
    public void MultiFileTemplatesTranspileTogether()
    {
        var sources = ReadTemplateSources().ToDictionary(t => t.Name, t => t.Source);

        var pairs = new[]
        {
            ("CanvasAnimation", "CanvasBindings"),
            ("ThreeScene", "ThreeBindings"),
            ("TestedTests", "TestedLogic"),
        };

        foreach (var (program, bindings) in pairs)
        {
            Assert.True(sources.ContainsKey(program), $"{program} was not found in the template file.");
            Assert.True(sources.ContainsKey(bindings), $"{bindings} was not found in the template file.");

            var documents = new[]
            {
                new SourceDocument($"{bindings}.csjs", sources[bindings]),
                new SourceDocument($"{program}.csjs", sources[program]),
            };

            var options = new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false };

            // The testing template references CSJS.Testing, which user projects reference too.
            if (program == "TestedTests")
                options.AdditionalReferences.Add(typeof(CSJS.Testing.Assert).Assembly.Location);

            var result = new Transpiler(options).Transpile(documents);

            Assert.True(result.Success,
                $"Template pair '{program}' + '{bindings}' does not transpile:\n  " +
                string.Join("\n  ", result.Errors.Select(e => e.ToString())));
        }
    }

    [Fact]
    public void EveryTemplateExportsAnEntryPoint()
    {
        // A template whose entry class is not exported produces a page that silently does nothing,
        // because the host script's import would resolve to undefined.
        foreach (var (name, source) in ReadTemplateSources())
        {
            if (name is "CanvasBindings" or "ThreeBindings" or "TestedTests") continue;

            Assert.True(source.Contains("[JsExport", StringComparison.Ordinal),
                $"Template '{name}' has no exported entry point.");
        }
    }

    // -------------------------------------------------------------------------------------------

    private static readonly Lazy<List<(string Name, string Source)>> Sources = new(Load);

    private static List<(string Name, string Source)> ReadTemplateSources() => Sources.Value;

    /// <summary>
    /// Pulls the <c>private const string Name = """…"""</c> declarations out of the template file.
    /// </summary>
    private static List<(string Name, string Source)> Load()
    {
        var path = FindTemplateFile();
        var text = File.ReadAllText(path);

        var pattern = new Regex(
            "private const string (?<name>\\w+) = \"\"\"\\r?\\n(?<body>.*?)\\r?\\n\\s*\"\"\";",
            RegexOptions.Singleline);

        var results = new List<(string, string)>();

        foreach (Match match in pattern.Matches(text))
        {
            var name = match.Groups["name"].Value;
            var body = match.Groups["body"].Value;

            // Only the C# templates are transpiled; the HTML ones are not source.
            if (name.EndsWith("Html", StringComparison.Ordinal)) continue;

            results.Add((name, Dedent(body)));
        }

        Assert.True(results.Count > 0, $"No template sources were found in {path}.");
        return results;
    }

    /// <summary>Removes the common leading indentation a raw string literal carries.</summary>
    private static string Dedent(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');

        var indent = lines
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l));
    }

    private static string FindTemplateFile()
    {
        // Walk up from the test binaries to the repository root.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "CSJSGen", "Models", "ProjectTemplates.Sources.cs");

            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate ProjectTemplates.Sources.cs.");
    }
}
