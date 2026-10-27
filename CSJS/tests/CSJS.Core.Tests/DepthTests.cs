using System.Text.RegularExpressions;
using CSJS.Core.Mapping;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// Phase 2 depth: the mapping table's integrity, the whole-program optimisations, and the
/// declaration output for the newer lowerings. Behaviour against .NET is in <see cref="ParityTests"/>.
/// </summary>
public class DepthTests
{
    // -----------------------------------------------------------------------------------------
    // MemberMap integrity
    // -----------------------------------------------------------------------------------------

    private static readonly Lazy<CSharpCompilation> Framework = new(() =>
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0 && File.Exists(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        return CSharpCompilation.Create("MapCheck", references: references);
    });

    /// <summary>
    /// A typo in a key never matches anything, so the member it was meant for silently stays
    /// unmapped. Every key must name a type and a member that exist, with that arity or exactly
    /// that signature.
    /// </summary>
    [Fact]
    public void EveryMemberMapKeyNamesARealMember()
    {
        var unresolved = MemberMap.AllKeys.Where(key => !Resolves(key)).ToList();
        Assert.True(unresolved.Count == 0, "Keys that match no framework member:\n  " + string.Join("\n  ", unresolved));
    }

    private static bool Resolves(string key)
    {
        var isConstructor = key.StartsWith("ctor:", StringComparison.Ordinal);
        if (isConstructor) key = key[5..];

        // Split "Owner.Member(sig)" / "Owner.Member/n" / "Owner(sig)" / "Owner/n" / "Owner.Member".
        string? signature = null;
        int? arity = null;
        var paren = key.IndexOf('(');
        if (paren >= 0)
        {
            signature = key[paren..];
            key = key[..paren];
        }
        else if (key.LastIndexOf('/') is var slash and >= 0)
        {
            var suffix = key[(slash + 1)..];
            if (suffix != "*") arity = int.Parse(suffix);
            key = key[..slash];
        }

        string ownerName;
        string? memberName;
        if (isConstructor)
        {
            ownerName = key;
            memberName = null;
        }
        else
        {
            var dot = key.LastIndexOf('.');
            ownerName = key[..dot];
            memberName = key[(dot + 1)..];
        }

        foreach (var owner in CandidateTypes(ownerName))
        {
            var members = isConstructor
                ? owner.InstanceConstructors.Cast<ISymbol>()
                : owner.GetMembers(memberName!);

            foreach (var member in members)
            {
                if (member is not IMethodSymbol method)
                {
                    if (signature is null && arity is null) return true;
                    continue;
                }

                var skipFirst = method.IsExtensionMethod;
                if (signature is not null)
                {
                    if (MemberMap.Signature(method, skipFirst) == signature) return true;
                    continue;
                }

                var count = method.Parameters.Length - (skipFirst ? 1 : 0);
                if (arity is null || count == arity) return true;
            }
        }

        return false;
    }

    /// <summary>Types an unbound owner name could mean: the plain name and every generic arity.</summary>
    private static IEnumerable<INamedTypeSymbol> CandidateTypes(string ownerName)
    {
        var compilation = Framework.Value;
        foreach (var suffix in new[] { "", "`1", "`2", "`3", "`4" })
        {
            if (compilation.GetTypeByMetadataName(ownerName + suffix) is { } type) yield return type;

            // A nested owner such as System.Console.Error is spelled with '+' in metadata.
            var lastDot = ownerName.LastIndexOf('.');
            if (lastDot > 0 && compilation.GetTypeByMetadataName(ownerName[..lastDot] + "+" + ownerName[(lastDot + 1)..] + suffix) is { } nested)
                yield return nested;
        }
    }

    /// <summary>
    /// A template that calls a helper the runtime does not export would throw a ReferenceError
    /// in the browser, which no transpile-time check would otherwise catch.
    /// </summary>
    [Fact]
    public void EveryHelperATemplateCallsIsExportedByTheRuntime()
    {
        var runtime = Transpiler.RuntimeSource.Value;
        var exported = Regex.Matches(runtime, @"export function (csjs\w+)").Select(m => m.Groups[1].Value).ToHashSet();

        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CSJS.Core", "Mapping", "MemberMap.cs"));
        var called = Regex.Matches(source, @"\bcsjs[A-Z]\w*(?=\()").Select(m => m.Value).ToHashSet();

        var missing = called.Except(exported).OrderBy(n => n).ToList();
        Assert.True(missing.Count == 0, "Helpers called by MemberMap but not exported: " + string.Join(", ", missing));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CSJS.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("CSJS.slnx not found above the test output.");
    }

    // -----------------------------------------------------------------------------------------
    // Whole-program optimisations
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void UnreferencedPrivateMembersAreLeftOut()
    {
        var js = Js.Emit("""
            public class Counter
            {
                private int _count;
                private static int Unused() => 42;
                private void AlsoUnused() { }
                private static readonly int SideEffect = Compute();
                private static int Compute() => 1;

                public int Next() => ++_count;
            }
            """);

        Js.DoesNotContain("Unused", js);
        Js.DoesNotContain("AlsoUnused", js);
        Js.Contains("_count", js);
        // An initialiser that calls something might have side effects, so the field stays —
        // and so does what it calls.
        Js.Contains("SideEffect", js);
        Js.Contains("Compute", js);
    }

    [Fact]
    public void RemovingUnusedMembersCanBeTurnedOff()
    {
        var js = Js.Emit("""
            public class Counter
            {
                private static int Unused() => 42;
                public int Value => 1;
            }
            """, new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false, RemoveUnusedPrivateMembers = false });

        Js.Contains("Unused", js);
    }

    [Fact]
    public void NonPublicEnumNeverTurnedIntoTextIsInlined()
    {
        var output = Js.Run("""
            using System;

            enum Direction { North, East, South, West }

            public static class Program
            {
                public static void Main()
                {
                    var d = Direction.South;
                    Console.WriteLine(d == Direction.South ? (int)d : -1);
                }
            }
            """);

        Assert.Equal("2", output);

        var js = Js.Emit("""
            enum Direction { North, East, South, West }
            public static class Program { public static int Main() => (int)Direction.West; }
            """);
        Js.DoesNotContain("Object.freeze", js);
        Js.Contains("3 /* Direction.West */", js);
    }

    [Fact]
    public void EnumThatIsPrintedKeepsItsObject()
    {
        var js = Js.Emit("""
            using System;
            enum Direction { North, East }
            public static class Program { public static void Main() => Console.WriteLine(Direction.East); }
            """);

        Js.Contains("Object.freeze", js);
    }

    // -----------------------------------------------------------------------------------------
    // Declarations for the new lowerings
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void RefAndOutAppearInDeclarationsAsTupleResults()
    {
        var dts = Js.EmitTypes("""
            public static class Parser
            {
                public static bool TryRead(string text, out int value) { value = text.Length; return true; }
                public static void Swap(ref int a, ref int b) { (a, b) = (b, a); }
            }
            """);

        Assert.Contains("static TryRead(text: string): [boolean, number];", dts);
        Assert.Contains("static Swap(a: number, b: number): [undefined, number, number];", dts);
    }

    [Fact]
    public void StructsDeclareTheirValueSemanticsHelpers()
    {
        var dts = Js.EmitTypes("""
            public struct Point { public int X; public int Y; }
            """);

        Assert.Contains("$clone(): Point;", dts);
        Assert.Contains("static $default(): Point;", dts);
        Assert.DoesNotContain("ValueType", dts);
    }

    [Fact]
    public void ReadonlyStructsAreSharedNotCopied()
    {
        var js = Js.Emit("""
            public readonly struct Size(int w, int h) { public int W => w; public int H => h; }
            public static class Program
            {
                public static int Area(Size s) { var copy = s; return copy.W * copy.H; }
            }
            """);

        // Copying an immutable value is unobservable, so it is not paid for.
        Js.DoesNotContain("$clone()", js.Split("$clone() {")[^1]);
    }

    // -----------------------------------------------------------------------------------------
    // Multi-folder projects
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Output mirrors the source tree, so modules in different folders must import each other —
    /// and the root-level runtime — by real relative paths. `./X.js` from everywhere only ever
    /// worked while every file sat in one folder.
    /// </summary>
    [Fact]
    public void ModulesInSubfoldersImportEachOtherByRelativePath()
    {
        var documents = new[]
        {
            new SourceDocument("Program.cs", """
                using System;
                using App.Models;
                public static class Program
                {
                    public static void Main()
                    {
                        var user = new User("Ana", 30);
                        Console.WriteLine(Format.Describe(user));
                    }
                }
                """),
            new SourceDocument("Models/User.cs", """
                namespace App.Models;
                public record User(string Name, int Age);
                """),
            new SourceDocument("Text/Format.cs", """
                using System.Linq;
                using App.Models;
                public static class Format
                {
                    public static string Describe(User user) => $"{user.Name.ToUpper()} ({new[] { user.Age }.Sum()})";
                }
                """),
        };

        var options = new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false, EmitEntryPointCall = true };
        var result = new Transpiler(options).Transpile(documents);
        Assert.True(result.Success, string.Join("\n", result.Errors));

        var format = result.Modules.Single(m => m.SourcePath == "Text/Format.cs").JavaScript;
        Js.Contains("from '../csjs-runtime.js'", format);

        var program = result.Modules.Single(m => m.SourcePath == "Program.cs").JavaScript;
        Js.Contains("from './Models/User.js'", program);
        Js.Contains("from './Text/Format.js'", program);

        var directory = Path.Combine(Path.GetTempPath(), "csjs-tests", Guid.NewGuid().ToString("n")[..10]);
        try
        {
            foreach (var module in result.Modules)
            {
                var target = Path.Combine(directory, Path.ChangeExtension(module.SourcePath, ".js"));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, module.JavaScript);
            }
            File.WriteAllText(Path.Combine(directory, "csjs-runtime.js"), result.RuntimeJavaScript ?? Transpiler.RuntimeSource.Value);
            File.WriteAllText(Path.Combine(directory, "package.json"), "{\"type\":\"module\"}");

            var start = new System.Diagnostics.ProcessStartInfo("node", "Program.js")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var node = System.Diagnostics.Process.Start(start)!;
            var stdout = node.StandardOutput.ReadToEnd();
            var stderr = node.StandardError.ReadToEnd();
            node.WaitForExit();

            Assert.True(node.ExitCode == 0, stderr);
            Assert.Equal("ANA (30)", stdout.Trim());
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    // -----------------------------------------------------------------------------------------
    // Interop and options
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ExtensionMethodsOnBindingTypesCallTheStaticClass()
    {
        var js = Js.Emit("""
            using CSJS.Browser;

            public static class ElementExtensions
            {
                public static void Hide(this Element element) => element.SetAttribute("hidden", "");
            }

            public static class Page
            {
                public static void Run() => Document.GetElementById("banner")?.Hide();
            }
            """);

        Js.Contains("static Hide(element)", js);
        Js.Contains("element.setAttribute('hidden', '')", js);
        Js.Contains("ElementExtensions.Hide(", js);
    }

    [Fact]
    public void CheckOverflowMakesArithmeticCheckedByDefault()
    {
        var program = Js.InMain("""
            int big = int.MaxValue;
            try { Console.WriteLine(big + 1); }
            catch (OverflowException) { Console.WriteLine("overflow"); }
            """);

        Assert.Equal("2147483648", Js.Run(program));

        var js = Js.Emit(program, new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false, CheckOverflow = true });
        Js.Contains("csjsChecked(big + 1, 'int')", js);
    }

    [Fact]
    public void UnmappedFrameworkExtensionMethodIsAnError()
    {
        var result = Js.Transpile("""
            using System;
            using System.Collections.Generic;

            public static class Sample
            {
                public static void Run()
                {
                    var queue = new PriorityQueue<string, int>();
                    queue.EnqueueRange(new[] { ("a", 1) });
                }
            }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Id == "CSJS0002");
    }
}
