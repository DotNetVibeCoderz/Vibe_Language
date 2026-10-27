using System.Diagnostics;
using CSJS.Core.Bundling;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>Writes files into a scratch directory and runs Node there.</summary>
internal sealed class NodeSandbox : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "csjs-bundle-tests", Guid.NewGuid().ToString("n")[..10]);

    public NodeSandbox()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Write("package.json", "{\"type\":\"module\"}");
    }

    public void Write(string relative, string content)
    {
        var path = Path.Combine(Directory, relative);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Writes every module of a transpile as the CLI lays them out.</summary>
    public void WriteModules(TranspileResult result)
    {
        foreach (var module in result.Modules)
        {
            var path = Bundler.OutputPathOf(module);
            Write(path, module.JavaScript);
            if (module.SourceMap is not null) Write(path + ".map", module.SourceMap);
        }
        Write("csjs-runtime.js", result.RuntimeJavaScript ?? Transpiler.RuntimeSource.Value);
    }

    public (int ExitCode, string Stdout, string Stderr) Run(string entry, params string[] nodeOptions)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            WorkingDirectory = Directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var option in nodeOptions) startInfo.ArgumentList.Add(option);
        startInfo.ArgumentList.Add(entry);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result.Replace("\r\n", "\n").TrimEnd(), stderr);
    }

    /// <summary>Runs and insists on success, returning stdout.</summary>
    public string RunOk(string entry)
    {
        var (code, stdout, stderr) = Run(entry);
        Assert.True(code == 0, $"node {entry} exited with {code}:\n{stderr}");
        return stdout;
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>
/// <c>cs2js build --bundle</c>: one file that behaves exactly like the modules it came from,
/// carries only the runtime it needs, and still maps back to the C#.
/// </summary>
public class BundlerTests
{
    private static readonly SourceDocument[] MultiFile =
    {
        new("Program.cs", """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public static class Program
            {
                public static void Main()
                {
                    var shapes = new List<Shape> { new Circle(1.5), new Square(2), new Circle(0.5) };
                    foreach (var s in shapes.OrderBy(s => s.Area()))
                        Console.WriteLine($"{s.Describe()}: {s.Area():F2}");
                    Console.WriteLine($"total {shapes.Sum(s => s.Area()):F3}, {7 / 2}");
                    Console.WriteLine(string.Format("{0} / {1:F1} // not a comment", "fmt", 3.14159));
                    Console.WriteLine(Registry.Label(Size.Large) + " " + Counter.Next() + Counter.Next());
                    try { Registry.Fail(); }
                    catch (InvalidOperationException e) { Console.WriteLine("caught " + e.Message); }
                }
            }
            """),
        new("Shape.cs", """
            public abstract class Shape
            {
                public abstract double Area();
                public abstract string Kind();
                public string Describe() => Kind() + "#" + Area().ToString("F1");
            }
            """),
        new("Shapes.cs", """
            using System;

            public sealed class Circle : Shape
            {
                private readonly double _r;
                public Circle(double r) { _r = r; }
                public override double Area() => Math.PI * _r * _r;
                public override string Kind() => "circle";
            }

            public sealed class Square : Shape
            {
                private readonly double _side;
                public Square(double side) { _side = side; }
                public override double Area() => _side * _side;
                public override string Kind() => "square";
            }
            """),
        new("Registry.cs", """
            using System;

            public enum Size { Small, Large }

            public static class Registry
            {
                public static string Label(Size size) => $"size={size} `tick` /* kept */";

                public static void Fail()
                {
                    throw new InvalidOperationException("boom");
                }
            }

            public static class Counter
            {
                private static int _n;
                public static int Next() { _n++; return _n; }
            }
            """),
    };

    private static TranspileResult TranspileProgram(SourceDocument[] documents)
    {
        var result = new Transpiler(new TranspileOptions { EmitTypeDefinitions = false, EmitEntryPointCall = true })
            .Transpile(documents);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return result;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BundleRunsLikeTheModules(bool minify)
    {
        var result = TranspileProgram(MultiFile);

        using var modules = new NodeSandbox();
        modules.WriteModules(result);
        var expected = modules.RunOk("Program.js");
        Assert.Contains("caught boom", expected);

        var bundle = Bundler.Bundle(result, new BundleOptions { Minify = minify });

        using var bundled = new NodeSandbox();
        bundled.Write("bundle.js", bundle.JavaScript);
        Assert.Equal(expected, bundled.RunOk("bundle.js"));

        // Dependencies come before what uses them: Shape before the classes extending it.
        var order = bundle.ModuleOrder.ToList();
        Assert.True(order.IndexOf("Shape.js") < order.IndexOf("Shapes.js"));
        Assert.Equal("Program.js", order[^1]);

        Assert.DoesNotContain("import ", bundle.JavaScript);
        Assert.DoesNotContain("csjs-runtime", bundle.JavaScript);
    }

    [Fact]
    public void OnlyUsedHelpersAreIncludedWithTheirDependencies()
    {
        var result = TranspileProgram(new[]
        {
            new SourceDocument("Program.cs", """
                using System;
                using System.Linq;

                public static class Program
                {
                    public static void Main()
                    {
                        var values = new[] { 1, 2, 3 };
                        Console.WriteLine(string.Format("{0:F2}", values.Sum()));
                    }
                }
                """),
        });

        var bundle = Bundler.Bundle(result);
        var included = bundle.RuntimeDeclarations;

        Assert.Contains("csjsFormat", included);
        // Not exported by the runtime, but csjsFormat calls it.
        Assert.Contains("csjsFormatNumber", included);
        Assert.DoesNotContain("csjsOrderBy", included);
        Assert.DoesNotContain("csjsGroupBy", included);
        Assert.DoesNotContain("csjsStringBuilder", included);
        Assert.True(included.Count < RuntimeLibrary.Default.Declarations.Count / 3,
            $"{included.Count} of {RuntimeLibrary.Default.Declarations.Count} runtime declarations were carried.");

        // Helpers become internal to the bundle; only the program's own API is exported.
        Assert.DoesNotContain("export function csjs", bundle.JavaScript);

        using var sandbox = new NodeSandbox();
        sandbox.Write("bundle.js", bundle.JavaScript);
        Assert.Equal("6.00", sandbox.RunOk("bundle.js"));
    }

    [Fact]
    public void EveryRuntimeHelperCanBeSelectedOnItsOwn()
    {
        // Each exported helper, extracted with its dependencies alone, must load: a missing
        // internal function or table would be a ReferenceError the moment it is called, and a
        // syntax error the moment the bundle is parsed.
        var runtime = RuntimeLibrary.Default;
        using var sandbox = new NodeSandbox();

        var script = new System.Text.StringBuilder();
        foreach (var helper in runtime.Declarations.Where(d => d.Exported))
        {
            var selected = runtime.Select(new[] { helper.Name });
            var body = string.Join("\n", selected.Select(d => d.Text));
            script.Append("new Function(").Append(System.Text.Json.JsonSerializer.Serialize(body + "\nreturn " + helper.Name + ";"))
                  .Append(")();\n");

            foreach (var reference in selected.SelectMany(d => d.References))
                Assert.Contains(selected, d => d.Name == reference);
        }

        sandbox.Write("check.js", script + "console.log('ok');");
        Assert.Equal("ok", sandbox.RunOk("check.js"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceMapLeadsBackToTheCSharpLine(bool minify)
    {
        var result = TranspileProgram(MultiFile);
        var bundle = Bundler.Bundle(result, new BundleOptions { Minify = minify });
        Assert.NotNull(bundle.SourceMap);

        var map = SourceMapDocument.Parse(bundle.SourceMap!);
        var lines = bundle.JavaScript.Split('\n');

        var throwLine = Array.FindIndex(lines, l => l.Contains("throw csjsError('InvalidOperationException'", StringComparison.Ordinal));
        Assert.True(throwLine >= 0, bundle.JavaScript);
        var column = lines[throwLine].IndexOf("throw", StringComparison.Ordinal);

        var original = map.OriginalPositionFor(throwLine, column);
        Assert.NotNull(original);
        Assert.Equal("Registry.cs", original!.Value.Source);

        // 0-based line 11 is `throw new InvalidOperationException("boom");`.
        var registry = MultiFile.Single(d => d.Path == "Registry.cs").Text.Replace("\r\n", "\n").Split('\n');
        Assert.Contains("throw new InvalidOperationException", registry[original.Value.Line]);

        // And Node agrees: with source maps on, the stack names the C# file and line.
        using var sandbox = new NodeSandbox();
        sandbox.Write("bundle.js", bundle.JavaScript);
        sandbox.Write("bundle.js.map", bundle.SourceMap!);
        sandbox.Write("throws.js", "import { Registry } from './bundle.js';\nRegistry.Fail();\n");
        var (code, _, stderr) = sandbox.Run("throws.js", "--enable-source-maps");
        Assert.NotEqual(0, code);
        Assert.Contains($"Registry.cs:{original.Value.Line + 1}", stderr);
    }

    [Fact]
    public void ExternalImportsAreHoistedAndMerged()
    {
        var modules = new[]
        {
            new BundleModule("A.js", "import { x } from 'pkg';\nimport { B } from './B.js';\nexport class A { static run() { return x + B.y(); } }\n", null),
            new BundleModule("B.js", "import { y as yy, x } from 'pkg';\nimport 'side-effect';\nexport class B { static y() { return yy + x; } }\n", null),
            new BundleModule("lib/C.js", "import { helper } from './native.js';\nexport class C { static h() { return helper(); } }\n", null),
        };

        var bundle = Bundler.Bundle(modules);

        Assert.Equal(new[] { "B.js", "A.js", "lib/C.js" }, bundle.ModuleOrder);
        Assert.Equal(new[] { "pkg", "side-effect", "./lib/native.js" }, bundle.ExternalImports);
        Assert.Contains("import { y as yy, x } from 'pkg';", bundle.JavaScript);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(bundle.JavaScript, "from 'pkg'"));
        Assert.DoesNotContain("./B.js", bundle.JavaScript);
    }

    [Fact]
    public void TopLevelNameCollisionsAreReportedNotMerged()
    {
        var modules = new[]
        {
            new BundleModule("A.js", "export class Thing {}\n", null),
            new BundleModule("B.js", "const Thing = 1;\nexport class Other {}\n", null),
        };

        var error = Assert.Throws<BundleException>(() => Bundler.Bundle(modules));
        Assert.Contains("Thing", error.Message);
    }
}
