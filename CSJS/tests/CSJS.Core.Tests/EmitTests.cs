using CSJS.Core;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// Tests about the shape of the emitted JavaScript. The spec asks for clean, readable,
/// tree-shakeable output, and these pin down what that means concretely.
/// </summary>
public class EmitTests
{
    [Fact]
    public void PublicTypesAreExported()
    {
        var js = Js.Emit("""
            public class Widget { }
            internal class Hidden { }
            """);

        Js.Contains("export class Widget", js);
        Js.Contains("class Hidden", js);
        Js.DoesNotContain("export class Hidden", js);
    }

    [Fact]
    public void OnlyUsedHelpersAreImported()
    {
        var js = Js.Emit(Js.InMain("""
            var text = "hello";
            Console.WriteLine(text.ToUpper());
            """));

        // A blanket runtime import would defeat tree-shaking, so a module that needs nothing
        // from the runtime must not import from it at all.
        Js.DoesNotContain("csjs-runtime", js);
    }

    [Fact]
    public void HelpersAreImportedIndividually()
    {
        var js = Js.Emit(Js.InMain("""
            var numbers = new List<int> { 3, 1, 2 };
            Console.WriteLine(numbers.Sum());
            """));

        // Named imports let a bundler drop the rest of the runtime.
        Js.Contains("import { csjsSum } from './csjs-runtime.js';", js);
    }

    [Fact]
    public void InterfacesAreErasedFromJavaScript()
    {
        var js = Js.Emit("""
            public interface IGreeter { string Greet(); }
            public class Greeter : IGreeter { public string Greet() => "hi"; }
            """);

        // An interface carries no runtime behaviour; emitting one would be dead weight.
        Js.DoesNotContain("IGreeter", js);
        Js.Contains("export class Greeter", js);
    }

    [Fact]
    public void InterfacesSurviveInTypeDefinitions()
    {
        var dts = Js.EmitTypes("""
            public interface IGreeter { string Greet(); }
            public class Greeter : IGreeter { public string Greet() => "hi"; }
            """);

        // Erased at runtime, preserved for the type system: this is the whole point of the
        // .d.ts half of the type-safety layer.
        Js.Contains("export interface IGreeter", dts);
        Js.Contains("Greet(): string;", dts);
        Js.Contains("implements IGreeter", dts);
    }

    [Fact]
    public void AutoPropertiesBecomePlainFields()
    {
        var js = Js.Emit("""
            public class User
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }
            """);

        // A getter/setter pair around plain storage would be noise.
        Js.Contains("Name = '';", js);
        Js.Contains("Age = 0;", js);
        Js.DoesNotContain("get Name()", js);
    }

    [Fact]
    public void ComputedPropertiesKeepTheirAccessors()
    {
        var js = Js.Emit("""
            public class Circle
            {
                public double Radius { get; set; }
                public double Area => 3.14159 * Radius * Radius;
            }
            """);

        Js.Contains("get Area()", js);
    }

    [Fact]
    public void LocalsPreferConstWhenNeverReassigned()
    {
        var js = Js.Emit(Js.InMain("""
            var fixedValue = 10;
            var counter = 0;
            counter++;
            Console.WriteLine(fixedValue + counter);
            """));

        Js.Contains("const fixedValue = 10;", js);
        Js.Contains("let counter = 0;", js);
    }

    [Fact]
    public void ChainedLinqDoesNotStackSpreads()
    {
        var js = Js.Emit(Js.InMain("""
            var numbers = new List<int> { 1, 2, 3 };
            var result = numbers.Where(n => n > 1).Select(n => n * 2).ToList();
            Console.WriteLine(result.Count);
            """));

        // `[...[...[...x]]]` is correct but unreadable; a List<T> is already an Array.
        Js.DoesNotContain("[...[...", js);
        Js.Contains(".filter(", js);
        Js.Contains(".map(", js);
    }

    [Fact]
    public void DocCommentsCarryThroughToJavaScript()
    {
        var js = Js.Emit("""
            /// <summary>Adds two numbers.</summary>
            public static class MathHelper
            {
                /// <summary>Returns the sum.</summary>
                public static int Add(int a, int b) => a + b;
            }
            """);

        Js.Contains("/** Adds two numbers. */", js);
        Js.Contains("/** Returns the sum. */", js);
    }

    [Fact]
    public void EnumEmitsBothDirections()
    {
        var js = Js.Emit("public enum Status { Draft = 0, Live = 1 }");

        Js.Contains("Draft: 0,", js);
        Js.Contains("Live: 1,", js);
        // The reverse map is what makes enum-to-string work.
        Js.Contains("0: 'Draft',", js);
        Js.Contains("1: 'Live',", js);
        Js.Contains("Object.freeze", js);
    }

    [Fact]
    public void ScriptModuleKindOmitsImportExport()
    {
        var options = new TranspileOptions
        {
            Module = ModuleKind.Script,
            EmitSourceMap = false,
            EmitTypeDefinitions = false,
        };

        var js = Js.Emit("public class Widget { }", options);

        Js.DoesNotContain("export ", js);
        Js.DoesNotContain("import ", js);
    }

    [Fact]
    public void JsExportGlobalWritesToGlobalThis()
    {
        var js = Js.Emit("""
            using CSJS;

            [JsExport(Global = true)]
            public static class App
            {
                public static void Start() { }
            }
            """);

        Js.Contains("globalThis.App = App;", js);
    }

    [Fact]
    public void GlobalShadowedByALocalIsReachedThroughGlobalThis()
    {
        // A [JsGlobal] member is written by its bare name, so a local of the same name would
        // capture it: `const screen = …; screen.width` read the local, not the global.
        var js = Js.Emit("""
            using CSJS;

            [JsGlobal("Screen")]
            public class Screen
            {
                public double Width => throw new JsExternException();
            }

            public static class Globals
            {
                [JsGlobal("screen")]
                public static Screen Screen => throw new JsExternException();
            }

            public static class App
            {
                public static double Shadowed()
                {
                    var screen = "a card";
                    return Globals.Screen.Width + screen.Length;
                }

                public static double Plain() => Globals.Screen.Width;
            }
            """);

        Js.Contains("globalThis.screen.width + screen.length", js);
        Js.Contains("return screen.width;", js);
    }

    [Fact]
    public void JsNameOverridesEmittedName()
    {
        var js = Js.Emit("""
            using CSJS;

            public class Api
            {
                [JsName("fetchData")]
                public void FetchData() { }
            }
            """);

        Js.Contains("fetchData()", js);
        Js.DoesNotContain("FetchData()", js);
    }

    /// <summary>
    /// Members of a binding declared in your own sources camelCase like any other binding.
    /// <para>
    /// They did not. Registration stored the declared spelling and ran before the camelCase rule
    /// ever applied, so <c>renderer.SetSize(...)</c> was emitted for a user-declared
    /// <c>[JsImport]</c> while <c>CSJS.Interop</c>'s own bindings emitted <c>setSize</c> — the
    /// difference being that a referenced assembly is never registered. It compiled, it emitted,
    /// and it failed only in the browser.
    /// </para>
    /// </summary>
    [Fact]
    public void BindingMembersDeclaredInSourceAreCamelCased()
    {
        var js = Js.Emit("""
            using CSJS;

            [JsImport("three", "WebGLRenderer")]
            public class WebGLRenderer
            {
                public WebGLRenderer() { }

                public void SetSize(double width, double height) => throw new JsExternException();
                public void SetPixelRatio(double ratio) => throw new JsExternException();
                public void Render() => throw new JsExternException();

                [JsName("domElement")] public object DomElement => throw new JsExternException();
            }

            public static class App
            {
                public static void Start()
                {
                    var renderer = new WebGLRenderer();
                    renderer.SetPixelRatio(2);
                    renderer.SetSize(800, 600);
                    renderer.Render();
                }
            }
            """);

        Js.Contains("renderer.setPixelRatio(2)", js);
        Js.Contains("renderer.setSize(800, 600)", js);
        Js.Contains("renderer.render()", js);

        // The PascalCase spellings are what three.js does not have.
        Js.DoesNotContain("SetPixelRatio", js);
        Js.DoesNotContain("SetSize", js);
    }

    [Fact]
    public void JsImportGeneratesModuleImport()
    {
        var js = Js.Emit("""
            using CSJS;

            [JsImport("three", "Scene")]
            public class Scene
            {
                public Scene() { }
            }

            public static class App
            {
                public static void Start()
                {
                    var scene = new Scene();
                }
            }
            """);

        Js.Contains("import { Scene } from 'three';", js);
        // The declaration is a binding for an existing JS class, not something to re-declare.
        Js.DoesNotContain("class Scene", js);
    }

    [Fact]
    public void JsBodyEmitsRawJavaScript()
    {
        var js = Js.Emit("""
            using CSJS;

            public static class Native
            {
                [JsBody("return globalThis.performance.now();")]
                public static double Now() => throw new JsExternException();
            }
            """);

        Js.Contains("return globalThis.performance.now();", js);
    }

    [Fact]
    public void JsIgnoreOmitsMembers()
    {
        var js = Js.Emit("""
            using CSJS;

            public class Service
            {
                public void Client() { }

                [JsIgnore]
                public void ServerOnly() { }
            }
            """);

        Js.Contains("Client()", js);
        Js.DoesNotContain("ServerOnly", js);
    }

    [Fact]
    public void AttributeClassesAreNotEmitted()
    {
        var js = Js.Emit("""
            using System;

            public sealed class MyMarkerAttribute : Attribute { }

            public class Widget { }
            """);

        // Attributes are compile-time metadata with no runtime presence.
        Js.DoesNotContain("MyMarkerAttribute", js);
        Js.Contains("export class Widget", js);
    }

    [Fact]
    public void UserExceptionExtendsError()
    {
        var js = Js.Emit("""
            using System;

            public class DomainException : Exception
            {
                public DomainException(string message) : base(message) { }
            }
            """);

        Js.Contains("class DomainException extends Error", js);
    }

    [Fact]
    public void OverloadsGetDistinctNames()
    {
        var js = Js.Emit("""
            public class Printer
            {
                public void Print() { }
                public void Print(string text) { }
                public void Print(string text, int copies) { }
            }
            """);

        // JavaScript has no overloading; each body needs its own name.
        Js.Contains("Print()", js);
        Js.Contains("Print$1(text)", js);
        Js.Contains("Print$2(text, copies)", js);
    }

    [Fact]
    public void OptionalParametersKeepTheirDefaults()
    {
        var js = Js.Emit("""
            public class Greeter
            {
                public string Greet(string name, string greeting = "Halo") => $"{greeting} {name}";
            }
            """);

        Js.Contains("Greet(name, greeting = 'Halo')", js);
    }

    [Fact]
    public void NamedArgumentsAreReorderedToDeclarationOrder()
    {
        var js = Js.Emit(Js.InMain("""
            Configure(verbose: true, name: "app");
            """, """
                private static void Configure(string name, bool verbose) { }
            """));

        // JavaScript is positional, so the call has to be rewritten to match.
        Js.Contains("Configure('app', true)", js);
    }
}

/// <summary>Tests for the source map, which the VSCode debugging bridge depends on.</summary>
public class SourceMapTests
{
    [Fact]
    public void SourceMapIsEmittedAndWellFormed()
    {
        var options = new TranspileOptions { EmitSourceMap = true, EmitTypeDefinitions = false };
        var result = new Transpiler(options).TranspileSource(Js.InMain("""
            var x = 1;
            Console.WriteLine(x);
            """));

        Assert.True(result.Success);
        var module = result.Modules[0];

        Assert.NotNull(module.SourceMap);
        Js.Contains("\"version\":3", module.SourceMap!);
        Js.Contains("\"mappings\":", module.SourceMap);
        Js.Contains("sourceMappingURL=Program.js.map", module.JavaScript);

        // An empty mappings string would satisfy the schema while making stepping useless.
        var mappingsStart = module.SourceMap.IndexOf("\"mappings\":\"", StringComparison.Ordinal) + 12;
        var mappingsEnd = module.SourceMap.IndexOf('"', mappingsStart);
        Assert.True(mappingsEnd - mappingsStart > 20, "The source map contains no usable mappings.");
    }

    [Fact]
    public void SourceMapEmbedsOriginalSource()
    {
        var options = new TranspileOptions { EmitSourceMap = true, EmbedSources = true, EmitTypeDefinitions = false };
        var result = new Transpiler(options).TranspileSource("public class Widget { }");

        Js.Contains("sourcesContent", result.Modules[0].SourceMap!);
        Js.Contains("Widget", result.Modules[0].SourceMap!);
    }

    [Fact]
    public void SourceMapCanBeDisabled()
    {
        var options = new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false };
        var result = new Transpiler(options).TranspileSource("public class Widget { }");

        Assert.Null(result.Modules[0].SourceMap);
        Js.DoesNotContain("sourceMappingURL", result.Modules[0].JavaScript);
    }
}

/// <summary>Tests for the TypeScript declaration output.</summary>
public class TypeDefinitionTests
{
    [Fact]
    public void PrimitiveTypesMapToTypeScript()
    {
        var dts = Js.EmitTypes("""
            public class Sample
            {
                public int Count { get; set; }
                public double Ratio { get; set; }
                public string Label { get; set; } = "";
                public bool Enabled { get; set; }
                public long Big { get; set; }
            }
            """);

        Js.Contains("Count: number;", dts);
        Js.Contains("Ratio: number;", dts);
        Js.Contains("Label: string;", dts);
        Js.Contains("Enabled: boolean;", dts);
        // 64-bit integers exceed Number's safe range, so they map to bigint.
        Js.Contains("Big: bigint;", dts);
    }

    [Fact]
    public void CollectionsMapToTheirJavaScriptCounterparts()
    {
        var dts = Js.EmitTypes("""
            using System.Collections.Generic;

            public class Sample
            {
                public List<string> Names { get; set; } = new();
                public Dictionary<string, int> Scores { get; set; } = new();
                public HashSet<int> Ids { get; set; } = new();
                public int[] Values { get; set; } = new int[0];
            }
            """);

        // These declarations have to match what the emitter actually produces.
        Js.Contains("Names: string[];", dts);
        Js.Contains("Scores: Map<string, number>;", dts);
        Js.Contains("Ids: Set<number>;", dts);
        Js.Contains("Values: number[];", dts);
    }

    [Fact]
    public void TasksMapToPromises()
    {
        var dts = Js.EmitTypes("""
            using System.Threading.Tasks;

            public class Api
            {
                public Task Save() => Task.CompletedTask;
                public Task<string> Load() => Task.FromResult("");
            }
            """);

        Js.Contains("Save(): Promise<void>;", dts);
        Js.Contains("Load(): Promise<string>;", dts);
    }

    [Fact]
    public void GenericsCarryTheirParameters()
    {
        var dts = Js.EmitTypes("""
            public class Box<T>
            {
                public T Value { get; set; } = default!;
                public TOut Convert<TOut>(TOut fallback) => fallback;
            }
            """);

        Js.Contains("export declare class Box<T>", dts);
        Js.Contains("Value: T;", dts);
        Js.Contains("Convert<TOut>(fallback: TOut): TOut;", dts);
    }

    [Fact]
    public void OptionalParametersAreMarkedOptional()
    {
        var dts = Js.EmitTypes("""
            public class Greeter
            {
                public string Greet(string name, string greeting = "Halo") => "";
            }
            """);

        Js.Contains("Greet(name: string, greeting?: string): string;", dts);
    }

    [Fact]
    public void IteratorsDeclareGeneratorReturnTypes()
    {
        var dts = Js.EmitTypes("""
            using System.Collections.Generic;

            public class Sequence
            {
                public IEnumerable<int> Numbers()
                {
                    yield return 1;
                }
            }
            """);

        // The emitted method is a generator, so declaring IEnumerable<int> as an array would lie.
        Js.Contains("Generator<number", dts);
    }

    [Fact]
    public void ReadOnlyPropertiesAreMarkedReadonly()
    {
        var dts = Js.EmitTypes("""
            public class Config
            {
                public string Name { get; } = "";
                public int Retries { get; set; }
            }
            """);

        Js.Contains("readonly Name: string;", dts);
        Js.Contains("Retries: number;", dts);
    }

    [Fact]
    public void EnumsDeclareBothDirections()
    {
        var dts = Js.EmitTypes("public enum Status { Draft = 0, Live = 1 }");

        Js.Contains("readonly Draft: 0;", dts);
        Js.Contains("readonly [ordinal: number]: string;", dts);
        Js.Contains("export type Status =", dts);
    }
}

/// <summary>Tests for diagnostics, which decide whether failures are honest or silent.</summary>
public class DiagnosticTests
{
    [Fact]
    public void CSharpErrorsAreReportedAndBlockOutput()
    {
        var result = Js.Transpile("public class Broken { public void M() { undefined(); } }");

        Assert.False(result.Success);
        Assert.Empty(result.Modules);
        Assert.Contains(result.Errors, e => e.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    [Fact]
    public void FrameworkRefAndOutParametersWithoutAMappingAreRejected()
    {
        // A user method's ref/out parameters are rewritten (see RefOutTests); a framework one has
        // no source to rewrite, so without a mapping it must still fail rather than lose the
        // write-back silently.
        var result = Js.Transpile("""
            using System.Threading;

            public class Sample
            {
                public static void Run()
                {
                    int x = 1;
                    Interlocked.Increment(ref x);
                }
            }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Id == "CSJS0002");
    }

    [Fact]
    public void PointersAreRejected()
    {
        var result = Js.Transpile("""
            public class Sample
            {
                public static unsafe void Run()
                {
                    int value = 1;
                    int* pointer = &value;
                }
            }
            """);

        Assert.False(result.Success);
    }

    [Fact]
    public void LockProducesAWarningButStillEmits()
    {
        var result = Js.Transpile("""
            using System;

            public class Sample
            {
                private static readonly object Gate = new object();

                public static void Run()
                {
                    lock (Gate)
                    {
                        Console.WriteLine("work");
                    }
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Id == "CSJS0006");
        Js.Contains("console.log('work')", result.Modules[0].JavaScript);
    }

    [Fact]
    public void DiagnosticsCarryFileAndPosition()
    {
        var result = Js.Transpile("public class Broken { public void M() { undefined(); } }");

        var error = result.Errors.First();
        Assert.Equal("Program.cs", error.FilePath);
        Assert.True(error.Line > 0);
        Assert.True(error.Column > 0);
    }
}

/// <summary>Tests for emitting several files at once, where cross-module references appear.</summary>
public class MultiModuleTests
{
    [Fact]
    public void EachDocumentBecomesItsOwnModule()
    {
        var result = new Transpiler(new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false })
            .Transpile(new[]
            {
                new SourceDocument("Models.csjs", "public class User { public string Name = \"\"; }"),
                new SourceDocument("Program.csjs", "public class Program { public static void Run() { var u = new User(); } }"),
            });

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal(2, result.Modules.Count);
        Assert.Equal("Models.js", result.Modules[0].FileName);
        Assert.Equal("Program.js", result.Modules[1].FileName);
    }

    [Fact]
    public void CrossModuleReferencesGenerateImports()
    {
        var result = new Transpiler(new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false })
            .Transpile(new[]
            {
                new SourceDocument("Models.csjs", "public class User { public string Name = \"\"; }"),
                new SourceDocument("Program.csjs", "public class Program { public static void Run() { var u = new User(); } }"),
            });

        var program = result.Modules.First(m => m.FileName == "Program.js");
        Js.Contains("import { User } from './Models.js';", program.JavaScript);
    }

    [Fact]
    public void NamespacesAreFlattenedButCollisionsStayDistinct()
    {
        var result = new Transpiler(new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false })
            .Transpile(new[]
            {
                new SourceDocument("A.csjs", "namespace Alpha { public class Service { } }"),
                new SourceDocument("B.csjs", "namespace Beta { public class Service { } }"),
            });

        Assert.True(result.Success, string.Join("\n", result.Errors));

        // JavaScript has no namespaces, so a collision must be disambiguated rather than
        // silently shadowed.
        var all = string.Join("\n", result.Modules.Select(m => m.JavaScript));
        Js.Contains("Alpha_Service", all);
        Js.Contains("Beta_Service", all);
    }
}
