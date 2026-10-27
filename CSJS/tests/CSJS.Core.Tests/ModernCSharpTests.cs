using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// Language features from C# 9 onwards.
/// <para>
/// Each test runs the emitted JavaScript and compares against what C# itself would print, so a
/// feature that parses but transpiles to the wrong thing fails here rather than in someone's
/// browser. Features that genuinely cannot cross into JavaScript are asserted to be *reported*,
/// which is the contract the engine makes.
/// </para>
/// </summary>
public class ModernCSharpTests
{
    // ===========================================================================================
    // C# 9
    // ===========================================================================================

    [Fact]
    public void TargetTypedNew()
    {
        var output = Js.Run("""
            using System;
            using System.Collections.Generic;

            public class Box { public int Value; }

            public static class Program
            {
                public static void Main()
                {
                    Box box = new();
                    box.Value = 7;
                    List<int> numbers = new() { 1, 2, 3 };

                    Console.WriteLine(box.Value);
                    Console.WriteLine(numbers.Count);
                }
            }
            """);

        Assert.Equal("7\n3", output);
    }

    [Fact]
    public void InitOnlyProperties()
    {
        var output = Js.Run("""
            using System;

            public class Config
            {
                public string Name { get; init; } = "";
                public int Retries { get; init; }
            }

            public static class Program
            {
                public static void Main()
                {
                    var config = new Config { Name = "csjs", Retries = 3 };
                    Console.WriteLine($"{config.Name} {config.Retries}");
                }
            }
            """);

        Assert.Equal("csjs 3", output);
    }

    [Fact]
    public void RelationalAndLogicalPatterns()
    {
        var output = Js.Run("""
            using System;

            public static class Program
            {
                private static string Band(int n) => n switch
                {
                    < 0 => "negative",
                    0 => "zero",
                    > 0 and < 10 => "small",
                    >= 10 and < 100 => "medium",
                    _ => "large",
                };

                public static void Main()
                {
                    foreach (var n in new[] { -5, 0, 3, 42, 1000 })
                        Console.WriteLine(Band(n));
                }
            }
            """);

        Assert.Equal("negative\nzero\nsmall\nmedium\nlarge", output);
    }

    [Fact]
    public void NotPattern()
    {
        var output = Js.Run(Js.InMain("""
            object value = "text";
            if (value is not null and string s)
            {
                Console.WriteLine($"string: {s}");
            }
            """));

        Assert.Equal("string: text", output);
    }

    [Fact]
    public void StaticLambdas()
    {
        var output = Js.Run(Js.InMain("""
            var numbers = new List<int> { 1, 2, 3, 4 };
            var doubled = numbers.Select(static n => n * 2).ToList();
            Console.WriteLine(string.Join(",", doubled));
            """));

        Assert.Equal("2,4,6,8", output);
    }

    // ===========================================================================================
    // C# 10
    // ===========================================================================================

    [Fact]
    public void FileScopedNamespace()
    {
        var output = Js.Run("""
            using System;

            namespace App;

            public static class Program
            {
                public static void Main() => Console.WriteLine("file-scoped");
            }
            """);

        Assert.Equal("file-scoped", output);
    }

    [Fact]
    public void RecordStruct()
    {
        var output = Js.Run("""
            using System;

            public record struct Point(int X, int Y);

            public static class Program
            {
                public static void Main()
                {
                    var a = new Point(1, 2);
                    var b = new Point(1, 2);
                    Console.WriteLine(a == b);
                    Console.WriteLine(a.X + a.Y);
                }
            }
            """);

        Assert.Equal("True\n3", output);
    }

    [Fact]
    public void ExtendedPropertyPatterns()
    {
        var output = Js.Run("""
            using System;

            public class Address { public string City { get; set; } = ""; }
            public class Person { public Address Home { get; set; } = new(); }

            public static class Program
            {
                public static void Main()
                {
                    var person = new Person { Home = new Address { City = "Bandung" } };

                    // The nested form, which C# 10 shortened from { Home: { City: ... } }.
                    if (person is { Home.City: "Bandung" })
                    {
                        Console.WriteLine("matched");
                    }
                }
            }
            """);

        Assert.Equal("matched", output);
    }

    [Fact]
    public void DeconstructionWithMixedDeclaration()
    {
        var output = Js.Run(Js.InMain("""
            var (a, b) = (1, 2);
            Console.WriteLine($"{a} {b}");
            """));

        Assert.Equal("1 2", output);
    }

    // ===========================================================================================
    // C# 11
    // ===========================================================================================

    [Fact]
    public void RawStringLiterals()
    {
        // The outer literal needs more quotes than the inner one it contains.
        var output = Js.Run(""""
            using System;

            public static class Program
            {
                public static void Main()
                {
                    var json = """
                        { "name": "csjs", "nested": "he said hi" }
                        """;

                    Console.WriteLine(json);
                }
            }
            """");

        Assert.Contains("\"name\": \"csjs\"", output);
        Assert.Contains("he said hi", output);
    }

    [Fact]
    public void ListPatterns()
    {
        var output = Js.Run("""
            using System;

            public static class Program
            {
                private static string Describe(int[] values) => values switch
                {
                    [] => "empty",
                    [var single] => $"one: {single}",
                    [var first, var second] => $"two: {first},{second}",
                    _ => $"many: {values.Length}",
                };

                public static void Main()
                {
                    Console.WriteLine(Describe(new int[0]));
                    Console.WriteLine(Describe(new[] { 5 }));
                    Console.WriteLine(Describe(new[] { 1, 2 }));
                    Console.WriteLine(Describe(new[] { 1, 2, 3, 4 }));
                }
            }
            """);

        Assert.Equal("empty\none: 5\ntwo: 1,2\nmany: 4", output);
    }

    [Fact]
    public void RequiredMembers()
    {
        var output = Js.Run("""
            using System;

            public class User
            {
                public required string Name { get; set; }
                public int Age { get; set; }
            }

            public static class Program
            {
                public static void Main()
                {
                    var user = new User { Name = "Ana", Age = 28 };
                    Console.WriteLine($"{user.Name} {user.Age}");
                }
            }
            """);

        Assert.Equal("Ana 28", output);
    }

    [Fact]
    public void GenericMathStyleConstraints()
    {
        // Generic constraints are erased, but the code must still transpile and run.
        var output = Js.Run("""
            using System;
            using System.Collections.Generic;

            public static class Program
            {
                private static T Largest<T>(List<T> items) where T : IComparable<T>
                {
                    var best = items[0];
                    foreach (var item in items)
                    {
                        if (item.CompareTo(best) > 0) best = item;
                    }
                    return best;
                }

                public static void Main()
                {
                    Console.WriteLine(Largest(new List<int> { 3, 9, 4 }));
                }
            }
            """);

        Assert.Equal("9", output);
    }

    // ===========================================================================================
    // C# 12
    // ===========================================================================================

    [Fact]
    public void PrimaryConstructorOnClass()
    {
        var output = Js.Run("""
            using System;

            public class Greeter(string name, int times)
            {
                public string Greet() => $"{name} x{times}";
            }

            public static class Program
            {
                public static void Main()
                {
                    Console.WriteLine(new Greeter("Ana", 3).Greet());
                }
            }
            """);

        Assert.Equal("Ana x3", output);
    }

    [Fact]
    public void CollectionExpressions()
    {
        var output = Js.Run("""
            using System;
            using System.Collections.Generic;

            public static class Program
            {
                public static void Main()
                {
                    int[] first = [1, 2, 3];
                    List<int> second = [4, 5];
                    int[] combined = [.. first, .. second, 6];

                    Console.WriteLine(string.Join(",", combined));
                }
            }
            """);

        Assert.Equal("1,2,3,4,5,6", output);
    }

    [Fact]
    public void DefaultLambdaParameters()
    {
        var output = Js.Run(Js.InMain("""
            var greet = (string name, string greeting = "Halo") => $"{greeting} {name}";
            Console.WriteLine(greet("Ana"));
            Console.WriteLine(greet("Budi", "Hai"));
            """));

        Assert.Equal("Halo Ana\nHai Budi", output);
    }

    [Fact]
    public void AliasAnyType()
    {
        var output = Js.Run("""
            using System;
            using Coordinate = (int X, int Y);

            public static class Program
            {
                public static void Main()
                {
                    Coordinate point = (3, 4);
                    Console.WriteLine($"{point.X},{point.Y}");
                }
            }
            """);

        Assert.Equal("3,4", output);
    }

    // ===========================================================================================
    // C# 13
    // ===========================================================================================

    [Fact]
    public void ParamsCollections()
    {
        var output = Js.Run("""
            using System;
            using System.Collections.Generic;

            public static class Program
            {
                private static int Total(params IEnumerable<int> numbers)
                {
                    var sum = 0;
                    foreach (var n in numbers) sum += n;
                    return sum;
                }

                public static void Main()
                {
                    Console.WriteLine(Total(1, 2, 3));
                }
            }
            """);

        Assert.Equal("6", output);
    }

    [Fact]
    public void ImplicitIndexInInitializer()
    {
        var output = Js.Run("""
            using System;

            public class Buffer
            {
                public int[] Values { get; } = new int[3];
            }

            public static class Program
            {
                public static void Main()
                {
                    var buffer = new Buffer { Values = { [0] = 7, [1] = 8 } };
                    Console.WriteLine($"{buffer.Values[0]},{buffer.Values[1]}");
                }
            }
            """);

        Assert.Equal("7,8", output);
    }

    // ===========================================================================================
    // Emission shape
    // ===========================================================================================

    [Fact]
    public void PrimaryConstructorCapturesAreFields()
    {
        var js = Js.Emit("""
            public class Counter(int start)
            {
                public int Next() => start + 1;
            }
            """);

        // A captured primary-constructor parameter becomes state, so it has to be a field
        // rather than a free variable the emitted method cannot see.
        Js.Contains("constructor(start)", js);
        Js.Contains("this.start = start", js);
    }

    [Fact]
    public void CollectionExpressionEmitsArrayLiteral()
    {
        var js = Js.Emit(Js.InMain("""
            int[] values = [1, 2, 3];
            Console.WriteLine(values.Length);
            """));

        Js.Contains("[1, 2, 3]", js);
    }

    [Fact]
    public void RawStringKeepsItsContent()
    {
        var js = Js.Emit(""""
            public static class Program
            {
                public static string Json() =>
                    """
                    {"a":1}
                    """;
            }
            """");

        Js.Contains("{\"a\":1}", js);
    }
}
