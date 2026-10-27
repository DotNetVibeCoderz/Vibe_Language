using CSJS.Cli;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// Incremental compilation is only worth having if it is indistinguishable from a full build.
/// Every test here checks the incremental output against a cold transpile of the same sources.
/// </summary>
public class TranspileSessionTests
{
    private static TranspileOptions Options() => new() { EmitTypeDefinitions = true, EmitSourceMap = true };

    private static readonly SourceDocument Models = new("Models.cs", """
        public sealed class Greeter
        {
            public string Prefix { get; set; } = "Hello";
            public string Greet(string name) => Prefix + ", " + name;
        }
        """);

    private static readonly SourceDocument Program = new("Program.cs", """
        using System;

        public static class Program
        {
            public static void Main()
            {
                var greeter = new Greeter();
                Console.WriteLine(greeter.Greet("world"));
            }
        }
        """);

    private static readonly SourceDocument Unrelated = new("Util.cs", """
        public static class Util
        {
            public static int Twice(int x) => x * 2;
        }
        """);

    private static void AssertSameAsFullBuild(TranspileResult incremental, IReadOnlyList<SourceDocument> documents)
    {
        var full = new Transpiler(Options()).Transpile(documents);
        Assert.True(full.Success);
        Assert.True(incremental.Success);
        Assert.Equal(full.Modules.Count, incremental.Modules.Count);

        for (var i = 0; i < full.Modules.Count; i++)
        {
            Assert.Equal(full.Modules[i].FileName, incremental.Modules[i].FileName);
            Assert.Equal(full.Modules[i].JavaScript, incremental.Modules[i].JavaScript);
            Assert.Equal(full.Modules[i].SourceMap, incremental.Modules[i].SourceMap);
            Assert.Equal(full.Modules[i].TypeDefinitions, incremental.Modules[i].TypeDefinitions);
        }

        Assert.Equal(full.RuntimeJavaScript is null, incremental.RuntimeJavaScript is null);
        Assert.Equal(full.Diagnostics.Select(d => d.ToString()), incremental.Diagnostics.Select(d => d.ToString()));
    }

    [Fact]
    public void ABodyEditReemitsOnlyThatFile()
    {
        var session = new TranspileSession(Options());
        var first = session.Transpile(new[] { Models, Program, Unrelated });
        Assert.True(first.Success);
        Assert.Equal(3, session.LastRun!.Emitted);
        Assert.False(session.LastRun.ReferencesReused);

        var edited = Program with { Text = Program.Text.Replace("\"world\"", "\"there\"") };
        var documents = new[] { Models, edited, Unrelated };
        var second = session.Transpile(documents);

        Assert.Equal(1, session.LastRun!.Emitted);
        Assert.Equal(2, session.LastRun.Reused);
        Assert.True(session.LastRun.ReferencesReused);
        Assert.True(session.LastRun.CompilationReused);
        Assert.False(session.LastRun.SurfaceChanged);
        Assert.Contains("there", second.Modules[1].JavaScript);

        AssertSameAsFullBuild(second, documents);
    }

    [Fact]
    public void ASignatureChangeReemitsTheFilesThatDependOnIt()
    {
        var session = new TranspileSession(Options());
        session.Transpile(new[] { Models, Program, Unrelated });

        // An overload changes the name the second Greet gets, and so what Program.js calls.
        var models = Models with
        {
            Text = Models.Text.Replace(
                "public string Greet(string name)",
                "public string Greet(int times) => Prefix + times;\n    public string Greet(string name)"),
        };
        var documents = new[] { models, Program, Unrelated };
        var result = session.Transpile(documents);

        Assert.True(session.LastRun!.SurfaceChanged);
        Assert.Equal(0, session.LastRun.Reused);
        Assert.Contains("Greet$1", result.Modules[1].JavaScript);

        AssertSameAsFullBuild(result, documents);
    }

    [Fact]
    public void AWholeProgramFactChangedByABodyEditReemitsTheDeclaringFile()
    {
        // An internal enum nobody turns into text is inlined as numbers in the file that
        // declares it. Interpolating it in a *different* file's body changes that — without
        // touching the declared surface at all.
        var enumFile = new SourceDocument("Mode.cs", """
            internal enum Mode { Off, On }
            internal static class Modes
            {
                public static Mode Default => Mode.On;
            }
            """);
        var user = new SourceDocument("Program.cs", """
            using System;
            public static class Program
            {
                public static void Main() { Console.WriteLine((int)Modes.Default); }
            }
            """);

        var session = new TranspileSession(Options());
        session.Transpile(new[] { enumFile, user });

        var stringifying = user with { Text = user.Text.Replace("(int)Modes.Default", "$\"{Modes.Default}\"") };
        var documents = new[] { enumFile, stringifying };
        var result = session.Transpile(documents);

        Assert.False(session.LastRun!.SurfaceChanged);
        Assert.Equal(0, session.LastRun.Reused);
        AssertSameAsFullBuild(result, documents);
    }

    [Fact]
    public void AFailedEditThenAFixRecovers()
    {
        var session = new TranspileSession(Options());
        session.Transpile(new[] { Models, Program });

        var broken = Program with { Text = Program.Text.Replace("greeter.Greet", "greeter.Grete") };
        var failed = session.Transpile(new[] { Models, broken });
        Assert.False(failed.Success);

        var fixedProgram = Program with { Text = Program.Text.Replace("\"world\"", "\"again\"") };
        var documents = new[] { Models, fixedProgram };
        var result = session.Transpile(documents);

        Assert.Equal(1, session.LastRun!.Reused);
        AssertSameAsFullBuild(result, documents);
    }

    [Fact]
    public void AddingAFileStartsAFreshCompilation()
    {
        var session = new TranspileSession(Options());
        session.Transpile(new[] { Models, Program });

        var documents = new[] { Models, Program, Unrelated };
        var result = session.Transpile(documents);

        Assert.False(session.LastRun!.CompilationReused);
        Assert.True(session.LastRun.SurfaceChanged);
        AssertSameAsFullBuild(result, documents);
    }

    [Fact]
    public void BodiesAreNotPartOfTheSurfaceButSignaturesAndPartialsAre()
    {
        static string SurfaceOf(string code)
            => TranspileSession.Surface(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code));

        Assert.Equal(
            SurfaceOf("class A { int F() { return 1; } int P => 2; int Q { get { return 3; } } }"),
            SurfaceOf("class A { int F() { return 41; } int P => 42; int Q { get { return 43; } } }"));

        Assert.NotEqual(
            SurfaceOf("class A { int F(int x) { return 1; } }"),
            SurfaceOf("class A { int F(long x) { return 1; } }"));

        // An auto-property and a computed one emit differently, so the shape of a body counts.
        Assert.NotEqual(SurfaceOf("class A { int P { get; } }"), SurfaceOf("class A { int P { get => 1; } }"));

        // Partial types are emitted from several files; any edit inside one counts.
        Assert.NotEqual(
            SurfaceOf("partial class A { int F() { return 1; } }"),
            SurfaceOf("partial class A { int F() { return 2; } }"));

        // Constants are inlined by callers.
        Assert.NotEqual(SurfaceOf("class A { const int N = 1; }"), SurfaceOf("class A { const int N = 2; }"));
    }
}

/// <summary>The build cache stamp: a build that would change nothing does nothing.</summary>
public class BuildCacheTests : IDisposable
{
    private readonly string _project = Path.Combine(Path.GetTempPath(), "csjs-cache-tests", Guid.NewGuid().ToString("n")[..10]);

    public BuildCacheTests()
    {
        Directory.CreateDirectory(Path.Combine(_project, "src"));
        File.WriteAllText(Path.Combine(_project, "csjs.json"), """{ "name": "CacheSample" }""");
        File.WriteAllText(Path.Combine(_project, "src", "Program.cs"), """
            using System;
            public static class Program
            {
                public static void Main() { Console.WriteLine("v1"); }
            }
            """);
        File.WriteAllText(Path.Combine(_project, "style.css"), "body { color: red; }");
    }

    public void Dispose()
    {
        try { Directory.Delete(_project, recursive: true); } catch { /* best effort */ }
    }

    private BuildReport Build(BuildSettings? settings = null)
    {
        var project = CsjsProject.Load(_project);
        var report = new BuildRunner(project, _project, settings: settings).Run(quiet: true);
        Assert.True(report.Success);
        return report;
    }

    [Fact]
    public void ANoOpBuildIsSkipped()
    {
        var first = Build();
        Assert.False(first.UpToDate);
        var js = Path.Combine(_project, "dist", "Program.js");
        var written = File.GetLastWriteTimeUtc(js);

        var second = Build();
        Assert.True(second.UpToDate);
        Assert.Equal(1, second.ModuleCount);
        Assert.Equal(written, File.GetLastWriteTimeUtc(js));

        // The stamp lives under obj/ and is never mistaken for an asset.
        Assert.False(Directory.Exists(Path.Combine(_project, "dist", "obj")));
    }

    [Fact]
    public void ChangedSourcesAssetsSettingsOrMissingOutputsRebuild()
    {
        Build();

        File.AppendAllText(Path.Combine(_project, "src", "Program.cs"), "\n// edited\n");
        Assert.False(Build().UpToDate);
        Assert.True(Build().UpToDate);

        File.WriteAllText(Path.Combine(_project, "style.css"), "body { color: blue; }");
        Assert.False(Build().UpToDate);
        Assert.Equal("body { color: blue; }", File.ReadAllText(Path.Combine(_project, "dist", "style.css")));

        File.Delete(Path.Combine(_project, "dist", "Program.js"));
        Assert.False(Build().UpToDate);
        Assert.True(File.Exists(Path.Combine(_project, "dist", "Program.js")));

        Assert.False(Build(new BuildSettings { Bundle = true }).UpToDate);
        Assert.True(File.Exists(Path.Combine(_project, "dist", "bundle.js")));

        Assert.False(Build(new BuildSettings { Force = true }).UpToDate);
    }

    [Fact]
    public void UnchangedOutputsAreNotRewritten()
    {
        Build();
        var map = Path.Combine(_project, "dist", "Program.js.map");
        var before = File.GetLastWriteTimeUtc(map);

        // A forced build produces identical text, so nothing on disk should be touched.
        var report = Build(new BuildSettings { Force = true });
        Assert.Equal(0, report.FilesWritten);
        Assert.True(report.FilesUnchanged >= 3);
        Assert.Equal(before, File.GetLastWriteTimeUtc(map));

        // Maps point back at the real sources, for stack traces and devtools.
        Assert.Contains("\"sourceRoot\":\"../src/\"", File.ReadAllText(map));
    }
}
