using System.Diagnostics;
using CSJS.Core.Bindings;
using CSJS.Core.Mapping;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// The binding generator: <c>.d.ts</c> in, C# bindings out.
/// <para>
/// Asserting on the generated C# is only the first check. The bindings exist to be compiled and
/// transpiled, so most tests push the generated file through the transpiler together with a small
/// program and check the JavaScript imports and calls the library correctly — and where a tiny
/// hand-written module can stand in for the library, run it on Node and compare the output.
/// </para>
/// </summary>
public class BindingGeneratorTests
{
    // A library declaration covering the constructs real typings lean on.
    private const string Library = """
        /** A 2D vector. */
        export declare class Vector2 {
            constructor(x?: number, y?: number);
            x: number;
            y: number;
            readonly length: number;
            static readonly ZERO: Vector2;
            /**
             * Adds another vector.
             * @param other the vector to add
             * @returns this vector, for chaining
             */
            add(other: Vector2): this;
            set(x: number, y: number): this;
            clone(): Vector2;
            static distance(a: Vector2, b: Vector2): number;
            delete(): void;
            toJSON(): { x: number; y: number };
        }

        /** Options for a renderer. */
        export interface RendererOptions {
            antialias?: boolean;
            alpha?: boolean;
            canvas?: HTMLCanvasElement;
            onFrame?(time: number): void;
            mode?: "fast" | "nice";
            readonly default?: string;
        }

        export declare class Renderer {
            constructor(options?: RendererOptions);
            render(scene: Vector2 | Vector2[]): string;
            readonly domElement: HTMLElement;
            setSize(width: number, height: number, updateStyle?: boolean): void;
            dispose(): Promise<void>;
            load(url: string): Promise<string>;
            on(event: string, handler: (e: Event) => void): () => void;
        }

        export declare class WebGLRenderer extends Renderer {
            readonly isWebGL: true;
            render(scene: Vector2): string;
        }

        export declare function createRenderer(options?: RendererOptions): Renderer;
        export declare function sum(...values: number[]): number;
        export declare const VERSION: string;
        export declare enum Mode { Fast, Nice = 4, Best }
        export declare enum Color { Red = "red", Green = "green" }
        export type Callback<T> = (value: T) => void;
        export declare function subscribe<T>(cb: Callback<T>): void;
        export type Id = string | number;
        export declare function find(id: Id): Vector2 | null;
        export interface Store<T> {
            get(): T;
            set(value: T): void;
            readonly size: number;
            [key: string]: unknown;
        }
        export declare function makeStore<T>(initial: T): Store<T>;
        export declare function useState<T>(initial: T | (() => T)): [T, (v: T) => void];
        export declare function useEffect(effect: () => void | (() => void), deps?: readonly unknown[]): void;
        export declare function mapValues(input: Record<string, number>): Map<string, Set<number>>;
        export declare function keys<T extends object>(o: T): Array<keyof T>;
        export default function init(): string;
        """;

    // A hand-written module that behaves the way the declarations above say it does.
    private const string LibraryJs = """
        export class Vector2 {
          constructor(x = 0, y = 0) { this.x = x; this.y = y; }
          get length() { return Math.hypot(this.x, this.y); }
          add(o) { this.x += o.x; this.y += o.y; return this; }
          set(x, y) { this.x = x; this.y = y; return this; }
          clone() { return new Vector2(this.x, this.y); }
          static distance(a, b) { return Math.hypot(a.x - b.x, a.y - b.y); }
          delete() { console.log('deleted'); }
          toJSON() { return { x: this.x, y: this.y }; }
        }
        Vector2.ZERO = new Vector2(0, 0);
        export class Renderer {
          constructor(options = {}) { this.options = options; }
          render(scene) { return Array.isArray(scene) ? `many:${scene.length}` : `one:${scene.x}`; }
          setSize(w, h, updateStyle) { console.log(`size ${w}x${h} ${updateStyle === undefined ? 'default' : updateStyle}`); }
        }
        export class WebGLRenderer extends Renderer { get isWebGL() { return true; } }
        export function createRenderer(options) {
          console.log(`antialias=${options.antialias} default=${options.default} keys=${Object.keys(options).join(',')}`);
          options.onFrame(16);
          return new Renderer(options);
        }
        export function sum(...values) { return values.reduce((a, b) => a + b, 0); }
        export const VERSION = '1.2.3';
        export function find(id) { return typeof id === 'string' ? new Vector2(1, 1) : null; }
        export function makeStore(initial) {
          let value = initial;
          return { get: () => value, set: v => { value = v; }, get size() { return 1; }, label: 'store' };
        }
        export function useState(initial) {
          let value = typeof initial === 'function' ? initial() : initial;
          return [value, v => console.log(`set ${v}`)];
        }
        export function useEffect(effect, deps) {
          const cleanup = effect();
          console.log(`deps=${deps === undefined ? 'none' : deps.length} cleanup=${typeof cleanup}`);
          if (typeof cleanup === 'function') cleanup();
        }
        export default function init() { return 'initialised'; }
        """;

    private const string LibraryProgram = """
        using System;
        using Bindings.Lib;

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(Lib.Init());

                var v = new Vector2(1, 2);
                v.Add(new Vector2(3, 4)).Set(v.X + 1, v.Y + 1);
                Console.WriteLine($"{v.X},{v.Y} len={v.Length}");
                Console.WriteLine(Vector2.Distance(new Vector2(3, 4), Vector2.ZERO));
                v.Delete();

                var renderer = Lib.CreateRenderer(new RendererOptions
                {
                    Antialias = true,
                    Default = "yes",
                    OnFrame = t => Console.WriteLine($"frame {t}"),
                });
                Console.WriteLine(renderer.Render(v));
                Console.WriteLine(renderer.Render(new[] { v, v }));
                renderer.SetSize(10, 20);
                renderer.SetSize(10, 20, false);

                Console.WriteLine(Lib.Sum(1, 2, 3));
                Console.WriteLine(Lib.VERSION);
                Console.WriteLine(Lib.Find("a") is null ? "none" : "found");
                Console.WriteLine(Lib.Find(1) is null ? "none" : "found");

                var store = Lib.MakeStore(5);
                store.Set(store.Get() + 1);
                Console.WriteLine($"{store.Get()} {store.Size} {store["label"]}");

                var (count, setCount) = Lib.UseState(() => 41);
                setCount(count + 1);
                Lib.UseEffect(() => Console.WriteLine("effect"));
                Lib.UseEffect(() =>
                {
                    Console.WriteLine("effect with cleanup");
                    return () => Console.WriteLine("cleanup");
                }, new object[] { count });

                Console.WriteLine((int)Mode.Best);
                Console.WriteLine(Color.Green);
            }
        }
        """;

    // -------------------------------------------------------------------------------------------
    // Generated C#
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void ClassBindsThroughJsImportWithIdiomaticNames()
    {
        var code = Generate(Library);

        Assert.Contains("[JsImport(\"lib\", \"Vector2\")]\npublic class Vector2", code);
        Assert.Contains("public Vector2(double? x = default, double? y = default) => throw new JsExternException();", code);
        Assert.Contains("public double X { get => throw new JsExternException(); set => throw new JsExternException(); }", code);

        // Read-only, static, and `this` returns.
        Assert.Contains("public double Length => throw new JsExternException();", code);
        Assert.Contains("[JsName(\"ZERO\")]\n    public static Vector2 ZERO", code);
        Assert.Contains("public virtual Vector2 Add(Vector2 other)", code);

        // camelCase is automatic for binding members, so no [JsName] on ordinary names.
        Assert.DoesNotContain("[JsName(\"add\")]", code);
        Assert.DoesNotContain("[JsName(\"delete\")]", code);
    }

    [Fact]
    public void JsDocBecomesXmlDocumentation()
    {
        var code = Generate(Library);
        Assert.Contains("/// <summary>A 2D vector.</summary>\n[JsImport", code);
        Assert.Contains("/// <summary>Adds another vector.</summary>", code);
        Assert.Contains("/// <param name=\"other\">the vector to add</param>", code);
        Assert.Contains("/// <returns>this vector, for chaining</returns>", code);
    }

    [Fact]
    public void OptionsInterfaceBecomesObjectLiteralWithExplicitNames()
    {
        var code = Generate(Library);

        Assert.Contains("[JsObjectLiteral]\npublic class RendererOptions", code);
        // Object literals get no automatic camelCase, so every renamed key is spelled out.
        Assert.Contains("[JsName(\"antialias\")]\n    public bool? Antialias { get; set; }", code);
        // A method on an options interface is a callback property.
        Assert.Contains("public Action<double>? OnFrame { get; set; }", code);
        // A string-literal union is a string; readonly becomes init.
        Assert.Contains("public string? Mode { get; set; }", code);
        Assert.Contains("[JsName(\"default\")]\n    public string? Default { get; init; }", code);
        // DOM types map onto the bindings that ship with CSJS.
        Assert.Contains("public Element? Canvas { get; set; }", code);
    }

    [Fact]
    public void ModuleFunctionsAndConstantsImportThemselves()
    {
        var code = Generate(Library);

        Assert.Contains("[JsImport(\"lib\")]\npublic static class Lib", code);
        Assert.Contains("[JsImport(\"lib\", \"createRenderer\")]\n    public static Renderer CreateRenderer(RendererOptions? options = default)", code);
        Assert.Contains("[JsImport(\"lib\", \"sum\")]\n    public static double Sum(params double[] values)", code);
        Assert.Contains("[JsImport(\"lib\", \"VERSION\")]\n    public static string VERSION => throw", code);
        Assert.Contains("[JsImport(\"lib\", Default = true)]\n    public static string Init()", code);
    }

    [Fact]
    public void UnionParametersBecomeOverloads()
    {
        var code = Generate(Library);

        Assert.Contains("public virtual string Render(Vector2 scene)", code);
        Assert.Contains("public virtual string Render(Vector2[] scene)", code);
        Assert.Contains("public static Vector2? Find(string id)", code);
        Assert.Contains("public static Vector2? Find(double id)", code);

        // A value or a factory for it — the shape of React's useState.
        Assert.Contains("public static (T, Action<T>) UseState<T>(T initial)", code);
        Assert.Contains("public static (T, Action<T>) UseState<T>(Func<T> initial)", code);

        // A callback that may return a cleanup function is two callbacks.
        Assert.Contains("public static void UseEffect(Action effect, object[]? deps = default)", code);
        Assert.Contains("public static void UseEffect(Func<Action> effect, object[]? deps = default)", code);
    }

    [Fact]
    public void TypesCrossBackThroughTheSharedVocabulary()
    {
        var code = Generate(Library);

        Assert.Contains("public virtual Task Dispose()", code);
        Assert.Contains("public virtual Task<string> Load(string url)", code);
        Assert.Contains("public virtual Action On(string @event, Action<Event> handler)", code);
        Assert.Contains("public static void Subscribe<T>(Action<T> cb)", code);
        Assert.Contains("Dictionary<string, HashSet<double>> MapValues(JsObject input)", code);
        // A Record is a plain object, not a Map, and the generated code says so.
        Assert.Contains("// note: 'Record<string, number>' is a plain object, not a Map; mapped to JsObject", code);
    }

    [Fact]
    public void InterfacesWithBehaviourBecomeCSharpInterfaces()
    {
        var code = Generate(Library);
        Assert.Contains("[JsGlobal(\"Object\")]\npublic interface Store<T>", code);
        Assert.Contains("    T Get();", code);
        Assert.Contains("    double Size { get; }", code);
        Assert.Contains("    object this[string key] { get; set; }", code);
    }

    [Fact]
    public void InheritanceHidesWithNewAndCopiesConstructors()
    {
        var code = Generate(Library);
        Assert.Contains("public class WebGLRenderer : Renderer", code);
        Assert.Contains("public WebGLRenderer(RendererOptions? options = default)", code);
        Assert.Contains("public new virtual string Render(Vector2 scene)", code);
    }

    [Fact]
    public void EnumsKeepTheirValues()
    {
        var code = Generate(Library);
        Assert.Contains("public enum Mode\n{\n    Fast,\n    Nice = 4,\n    Best,\n}", code);
        Assert.Contains("public const string Red = \"red\";", code);
    }

    [Fact]
    public void InlineObjectTypeGetsANamedShape()
    {
        var code = Generate(Library);
        Assert.Contains("public virtual Vector2ToJSON ToJSON()", code);
        Assert.Contains("[JsObjectLiteral]\npublic class Vector2ToJSON", code);
    }

    // -------------------------------------------------------------------------------------------
    // Compiled, transpiled, and run
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void GeneratedBindingsTranspileToCorrectImportsAndCalls()
    {
        var bindings = Generate(Library);
        var result = TranspileWith(bindings, LibraryProgram);
        var js = result.Modules.Single(m => m.FileName == "Program.js").JavaScript;

        Js.Contains("import init, { VERSION as version, Vector2, createRenderer, find, makeStore, sum, useEffect, useState } from 'lib';", js);
        Js.Contains("new Vector2(1, 2)", js);
        Js.Contains("v.add(new Vector2(3, 4)).set(", js);
        Js.Contains("Vector2.distance(new Vector2(3, 4), Vector2.ZERO)", js);
        Js.Contains("v.delete()", js);
        Js.Contains("createRenderer({ antialias: true, default: 'yes', onFrame: ", js);
        Js.Contains("renderer.render(v)", js);
        Js.Contains("renderer.setSize(10, 20)", js);
        Js.Contains("sum(1, 2, 3)", js);
        Js.Contains("console.log(version)", js);
        Js.Contains("store['label']", js);
        Js.Contains("init()", js);

        // Binding overloads share one JavaScript name; nothing is suffixed.
        Js.DoesNotContain("$1", js);
        Js.DoesNotContain("Lib.", js);
    }

    [Fact]
    public void GeneratedBindingsRunAgainstARealModule()
    {
        var bindings = Generate(Library, module: "./lib.js");
        var output = RunWith(bindings, LibraryProgram, ("lib.js", LibraryJs));

        Assert.Equal("""
            initialised
            5,7 len=8.602325267042627
            5
            deleted
            antialias=true default=yes keys=antialias,default,onFrame
            frame 16
            one:5
            many:2
            size 10x20 default
            size 10x20 false
            6
            1.2.3
            found
            none
            6 1 store
            set 42
            effect
            deps=none cleanup=undefined
            effect with cleanup
            deps=1 cleanup=function
            cleanup
            5
            green
            """.Replace("\r\n", "\n"), output);
    }

    // -------------------------------------------------------------------------------------------
    // Declaration forms
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void GlobalScriptBindsThroughJsGlobal()
    {
        const string declarations = """
            declare function greet(name: string): string;
            declare var counter: number;
            declare class Widget {
                constructor(id: string);
                render(): void;
            }
            declare namespace Tools {
                function slugify(text: string): string;
                namespace Inner { const depth: number; }
            }
            """;

        var code = Generate(declarations, module: null);
        Assert.Contains("[JsGlobal]\npublic static class Index", code);
        Assert.Contains("[JsGlobal(\"greet\")]\n    public static string Greet(string name)", code);
        Assert.Contains("[JsGlobal(\"counter\")]\n    public static double Counter { get => throw", code);
        Assert.Contains("[JsGlobal(\"Widget\")]\npublic class Widget", code);
        Assert.Contains("[JsGlobal(\"Tools\")]\npublic static class Tools", code);
        Assert.Contains("[JsGlobal(\"Tools.Inner\")]", code);

        var js = TranspileWith(code, """
            using Bindings.Index;
            public static class Program
            {
                public static void Main()
                {
                    System.Console.WriteLine(Index.Greet("x") + Tools.Slugify("A B") + Inner.Depth);
                    new Widget("w").Render();
                    Index.Counter = 3;
                }
            }
            """).Modules.Single(m => m.FileName == "Program.js").JavaScript;

        Js.Contains("greet('x') + Tools.slugify('A B') + Tools.Inner.depth", js);
        Js.Contains("new Widget('w').render()", js);
        Js.Contains("counter = 3", js);
        Js.DoesNotContain("import", js.Split('\n').Where(l => !l.StartsWith("//")).FirstOrDefault() ?? "");
    }

    [Fact]
    public void UmdNamespaceBindsAsGlobalWhenAsked()
    {
        const string declarations = """
            export = Confetti;
            export as namespace Confetti;
            declare namespace Confetti {
                interface Options { particleCount?: number; spread?: number; }
                function fire(options?: Options): Promise<null>;
                function reset(): void;
            }
            """;

        var module = Generate(declarations, module: "canvas-confetti");
        Assert.Contains("[JsImport(\"canvas-confetti\", \"fire\")]", module);

        var global = BindingGenerator.Generate(new BindingSource("confetti.d.ts", declarations), new BindingOptions { Global = true }).Code;
        Assert.Contains("[JsGlobal(\"Confetti\")]\npublic static class Confetti", global);
        Assert.Contains("public static Task<object> Fire(Options? options = default)", global);
    }

    [Fact]
    public void AmbientModuleNamesItsOwnSpecifier()
    {
        const string declarations = """
            declare module "tiny-lib" {
                export function double(x: number): number;
                export interface Config { verbose: boolean }
            }
            """;

        var result = BindingGenerator.Generate(new BindingSource("types.d.ts", declarations));
        Assert.Equal("TinyLib", result.ContainerName);
        Assert.Contains("[JsImport(\"tiny-lib\", \"double\")]\n    public static double Double(double x)", result.Code);
        Assert.Contains("public class Config", result.Code);
    }

    [Fact]
    public void ReExportsAreFollowedThroughTheResolver()
    {
        var files = new Dictionary<string, string>
        {
            ["/pkg/index.d.ts"] = """
                export * from './core';
                export { helper as assist } from './util';
                import { Thing } from './core';
                export declare function make(): Thing;
                """,
            ["/pkg/core.d.ts"] = """
                export declare class Thing { name: string; }
                export declare function create(name: string): Thing;
                """,
            ["/pkg/util.d.ts"] = "export declare function helper(): void;",
        };

        var options = new BindingOptions
        {
            Module = "pkg",
            Resolve = (from, specifier) =>
            {
                var path = "/pkg/" + specifier.TrimStart('.', '/') + ".d.ts";
                return files.TryGetValue(path, out var text) ? new BindingSource(path, text) : null;
            },
        };

        var code = BindingGenerator.Generate(new BindingSource("/pkg/index.d.ts", files["/pkg/index.d.ts"]), options).Code;
        Assert.Contains("[JsImport(\"pkg\", \"Thing\")]\npublic class Thing", code);
        Assert.Contains("[JsImport(\"pkg\", \"create\")]", code);
        Assert.Contains("[JsImport(\"pkg\", \"assist\")]\n    public static void Assist()", code);
        Assert.Contains("public static Thing Make()", code);
    }

    [Fact]
    public void UnbindableConstructsAreReportedNeverDropped()
    {
        const string declarations = """
            export declare class Box<T> {
                [Symbol.iterator](): Iterator<T>;
                value: T extends string ? number : boolean;
                map<U>(fn: (value: T) => U): Box<U>;
            }
            export type Getters<T> = { [K in keyof T]: () => T[K] };
            export declare function getters<T>(o: T): Getters<T>;
            export declare function broken(: void;
            """;

        var result = BindingGenerator.Generate(new BindingSource("box.d.ts", declarations), new BindingOptions { Module = "box" });
        Assert.Contains("// skipped: [Symbol.iterator] — computed property names have no C# equivalent", result.Code);
        // A conditional type maps through its general branch, and says so.
        Assert.Contains("// note: conditional type 'T extends string ? number : boolean' mapped through its general branch", result.Code);
        Assert.Contains("public bool Value { get =>", result.Code);
        Assert.Contains("mapped type", result.Code);
        Assert.Contains(result.Skipped, s => s.Contains("broken"));
        Assert.Contains("public virtual Box<U> Map<U>(Func<T, U> fn)", result.Code);

        // It still compiles.
        Assert.True(TranspileWith(result.Code, "public static class Program { public static void Main() { } }").Success);
    }

    [Fact]
    public void KeywordsAreEscapedAndCollisionsRenamed()
    {
        const string declarations = """
            export declare class Thing {
                static create(): Thing;
                create(): void;
                thing: string;
                "data-id": string;
                $el: HTMLElement;
                event(event: string, object: unknown, params: string[]): void;
            }
            """;

        var code = Generate(declarations);
        Assert.Contains("public static Thing Create()", code);
        Assert.Contains("[JsName(\"create\")]\n    public virtual void Create_()", code);
        Assert.Contains("public string Thing_", code);
        Assert.Contains("[JsName(\"data-id\")]\n    public string Data_id", code);
        Assert.Contains("[JsName(\"$el\")]\n    public Element _el", code);
        Assert.Contains("public virtual void Event(string @event, object @object, string[] @params)", code);

        Assert.True(TranspileWith(code, "public static class Program { public static void Main() { } }").Success);
    }

    [Fact]
    public void GenericDefaultsFillMissingArguments()
    {
        const string declarations = """
            export interface Ref<T = any> { value: T }
            export declare function plain(r: Ref): void;
            export declare function typed(r: Ref<string>): Ref<number>;
            """;

        var code = Generate(declarations);
        Assert.Contains("public class Ref<T>", code);
        Assert.Contains("public static void Plain(Ref<object> r)", code);
        Assert.Contains("public static Ref<double> Typed(Ref<string> r)", code);
    }

    // -------------------------------------------------------------------------------------------
    // One type table
    // -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("string")]
    [InlineData("number")]
    [InlineData("boolean")]
    [InlineData("bigint")]
    [InlineData("string[]")]
    [InlineData("Promise<string>")]
    [InlineData("Promise<void>")]
    [InlineData("Map<string, number>")]
    [InlineData("Set<string>")]
    [InlineData("Date")]
    [InlineData("() => void")]
    [InlineData("(arg0: string) => void")]
    [InlineData("(arg0: number, arg1: string) => boolean")]
    [InlineData("[string, number]")]
    [InlineData("number | null")]
    public void FromTypeScriptRoundTripsThroughToTypeScript(string typeScript)
    {
        // TypeScript → C# through the generator, then C# → TypeScript through the .d.ts emitter's
        // own mapping. Landing back where it started is what "one type table" means.
        // A property of an object-literal shape, because a function-typed constant binds as a method.
        var code = BindingGenerator.Generate($"export interface Holder {{ value: {typeScript} }}", new BindingOptions { Module = "m" }).Code;
        var declaration = code.Split('\n').Single(l => l.Contains(" Value { get; set; }")).Trim();
        var csharpType = declaration["public ".Length..declaration.IndexOf(" Value ", StringComparison.Ordinal)];

        var tree = CSharpSyntaxTree.ParseText($"using System; using System.Collections.Generic; using System.Threading.Tasks; class C {{ {csharpType} F; }}");
        var compilation = CSharpCompilation.Create("RoundTrip", new[] { tree },
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var field = (IFieldSymbol)compilation.GetTypeByMetadataName("C")!.GetMembers("F").Single();

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Equal(typeScript, TypeMap.ToTypeScript(field.Type));
    }

    [Fact]
    public void RestCallbacksSpreadIntoOneOverloadPerArity()
    {
        // SignalR's `on(name, (...args: any[]) => any)` and socket.io's `onAny` — before, the rest
        // parameter was dropped and the callback received nothing. The constructors are
        // socket.io-client's Manager: a union overload that expands into a duplicate of an
        // explicit one differing only in nullability, which C# rejects (CS0111).
        const string declarations = """
            export interface Options { path?: string; }
            export declare class Hub {
              constructor(opts: Options);
              constructor(uri?: string, opts?: Options);
              constructor(uri?: string | Options, opts?: Options);
              on(name: string, handler: (...args: any[]) => any): void;
              off(name: string, handler: (...args: any[]) => void): void;
              fire(name: string, ...args: any[]): void;
            }
            """;
        const string module = """
            export class Hub {
              constructor(uri, opts) { this.handlers = new Map(); this.uri = typeof uri === 'string' ? uri : '(none)'; }
              on(name, handler) { this.handlers.set(name, handler); }
              off(name) { this.handlers.delete(name); }
              fire(name, ...args) { const h = this.handlers.get(name); console.log(h ? `${name} -> ${h(...args)}` : `${name} unhandled`); }
            }
            """;
        const string program = """
            using System;
            using Bindings.Lib;

            public static class Program
            {
                public static void Main()
                {
                    var hub = new Hub("/hub");
                    hub.On("none", () => Console.WriteLine("none called"));
                    hub.On("one", (object a) => Console.WriteLine($"one {a}"));
                    hub.On("two", (object a, object b) => $"{a}+{b}");
                    hub.Fire("none");
                    hub.Fire("one", "x");
                    hub.Fire("two", 1, 2);
                    hub.Off("two", (object a) => { });
                    hub.Fire("two", 1, 2);
                }
            }
            """;

        var bindings = Generate(declarations, module: "./lib.js");
        Assert.Contains("public virtual void On(string name, Action<object> handler)", bindings);
        Assert.Contains("public virtual void On(string name, Func<object, object, object> handler)", bindings);
        Assert.Contains("public virtual void Off(string name, Action<object, object, object> handler)", bindings);
        Assert.DoesNotContain("rest parameter '...args' dropped", bindings);

        var output = RunWith(bindings, program, ("lib.js", module));
        Assert.Equal("none called\nnone -> undefined\none x\none -> undefined\ntwo -> 1+2\ntwo unhandled", output);
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    private static string Generate(string declarations, string? module = "lib")
        => BindingGenerator.Generate(new BindingSource("index.d.ts", declarations), new BindingOptions { Module = module })
            .Code.Replace("\r\n", "\n");

    private static TranspileResult TranspileWith(string bindings, string program)
    {
        var options = new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false, EmitEntryPointCall = true };
        var result = new Transpiler(options).Transpile(new[]
        {
            new SourceDocument("Program.cs", program),
            new SourceDocument("Bindings.cs", bindings),
        });

        Assert.True(result.Success,
            "Transpilation failed:\n  " + string.Join("\n  ", result.Errors.Select(e => e.ToString())) + "\n\nBindings:\n" + bindings);
        return result;
    }

    /// <summary>Transpiles bindings plus a program, writes them beside hand-written modules, and runs Node.</summary>
    private static string RunWith(string bindings, string program, params (string Name, string Source)[] modules)
    {
        var result = TranspileWith(bindings, program);
        var directory = Path.Combine(Path.GetTempPath(), "csjs-bind-tests", Guid.NewGuid().ToString("n")[..10]);
        Directory.CreateDirectory(directory);

        try
        {
            foreach (var module in result.Modules)
                File.WriteAllText(Path.Combine(directory, module.FileName), module.JavaScript);
            File.WriteAllText(Path.Combine(directory, "csjs-runtime.js"), result.RuntimeJavaScript ?? Transpiler.RuntimeSource.Value);
            File.WriteAllText(Path.Combine(directory, "package.json"), "{\"type\":\"module\"}");
            foreach (var (name, source) in modules)
                File.WriteAllText(Path.Combine(directory, name), source);

            var startInfo = new ProcessStartInfo("node", "Program.js")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            var program_ = result.Modules.Single(m => m.FileName == "Program.js").JavaScript;
            Assert.True(process.ExitCode == 0, $"Node exited with {process.ExitCode}:\n{stderr}\n\nEmitted:\n{program_}");
            return stdout.Replace("\r\n", "\n").TrimEnd();
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }
}
