using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// Behaviour tests: the emitted JavaScript actually runs and produces what the C# would.
/// These are the tests that catch a transpiler being plausible but wrong.
/// </summary>
public class SemanticsTests
{
    [Fact]
    public void IntegerDivisionTruncates()
    {
        var output = Js.Run(Js.InMain("""
            int a = 7, b = 2;
            Console.WriteLine(a / b);
            Console.WriteLine(-7 / 2);
            Console.WriteLine(7.0 / 2);
            """));

        // JavaScript's / would give 3.5; C# gives 3, and -3 rather than -4.
        Assert.Equal("3\n-3\n3.5", output);
    }

    [Fact]
    public void CompoundIntegerDivisionTruncates()
    {
        var output = Js.Run(Js.InMain("""
            var x = 10;
            x /= 3;
            Console.WriteLine(x);
            """));

        Assert.Equal("3", output);
    }

    [Fact]
    public void StringInterpolationFormatsNumbers()
    {
        var output = Js.Run(Js.InMain("""
            var value = 3.14159;
            Console.WriteLine($"{value:F2}");
            Console.WriteLine($"{42}");
            """));

        Assert.Equal("3.14\n42", output);
    }

    [Fact]
    public void EnumInterpolatesAsItsName()
    {
        var output = Js.Run("""
            using System;

            public enum Level { Low = 1, Medium = 2, High = 3 }

            public static class Program
            {
                public static void Main()
                {
                    var level = Level.Medium;
                    Console.WriteLine($"{level}");
                    Console.WriteLine((int)level);
                }
            }
            """);

        // The enum is a number at runtime, so without the reverse map this would print 2.
        Assert.Equal("Medium\n2", output);
    }

    [Fact]
    public void ListMapsToArray()
    {
        var output = Js.Run(Js.InMain("""
            var items = new List<string> { "b", "a", "c" };
            items.Add("d");
            items.Sort();
            Console.WriteLine(string.Join(",", items));
            Console.WriteLine(items.Count);
            items.RemoveAt(0);
            Console.WriteLine(string.Join(",", items));
            """));

        Assert.Equal("a,b,c,d\n4\nb,c,d", output);
    }

    [Fact]
    public void ListSortIsNumericNotLexical()
    {
        var output = Js.Run(Js.InMain("""
            var numbers = new List<int> { 10, 9, 100, 2 };
            numbers.Sort();
            Console.WriteLine(string.Join(",", numbers));
            """));

        // JavaScript's default sort is lexical and would produce 10,100,2,9.
        Assert.Equal("2,9,10,100", output);
    }

    [Fact]
    public void DictionaryMapsToMap()
    {
        var output = Js.Run(Js.InMain("""
            var ages = new Dictionary<string, int>();
            ages["ana"] = 30;
            ages["budi"] = 25;
            Console.WriteLine(ages.Count);
            Console.WriteLine(ages["ana"]);
            Console.WriteLine(ages.ContainsKey("budi"));
            foreach (var entry in ages)
            {
                Console.WriteLine($"{entry.Key}={entry.Value}");
            }
            """));

        Assert.Equal("2\n30\nTrue\nana=30\nbudi=25", output);
    }

    [Fact]
    public void TryGetValueBindsTheOutVariable()
    {
        var output = Js.Run(Js.InMain("""
            var scores = new Dictionary<string, int>();
            scores["x"] = 5;

            if (scores.TryGetValue("x", out var found))
            {
                Console.WriteLine($"found {found}");
            }

            if (!scores.TryGetValue("missing", out var absent))
            {
                Console.WriteLine("missing not found");
            }
            """));

        Assert.Equal("found 5\nmissing not found", output);
    }

    [Fact]
    public void LinqChainsProduceCorrectValues()
    {
        var output = Js.Run(Js.InMain("""
            var numbers = new List<int> { 5, 3, 8, 1, 9, 2 };

            var evens = numbers.Where(n => n % 2 == 0).OrderBy(n => n).ToList();
            Console.WriteLine(string.Join(",", evens));
            Console.WriteLine(numbers.Sum());
            Console.WriteLine(numbers.Max());
            Console.WriteLine(numbers.Any(n => n > 8));
            Console.WriteLine(numbers.Count(n => n < 5));
            Console.WriteLine(string.Join(",", numbers.Take(2)));
            """));

        Assert.Equal("2,8\n28\n9\nTrue\n3\n5,3", output);
    }

    [Fact]
    public void RecordsCompareByValue()
    {
        var output = Js.Run("""
            using System;

            public record Point(int X, int Y);

            public static class Program
            {
                public static void Main()
                {
                    var a = new Point(1, 2);
                    var b = new Point(1, 2);
                    var c = new Point(3, 4);

                    Console.WriteLine(a == b);
                    Console.WriteLine(a == c);
                    Console.WriteLine(a.ToString());
                }
            }
            """);

        // Reference equality would report false for the first comparison.
        Assert.Equal("True\nFalse\nPoint { X = 1, Y = 2 }", output);
    }

    [Fact]
    public void CustomToStringIsUsedByInterpolation()
    {
        var output = Js.Run("""
            using System;

            public class Money
            {
                public decimal Amount { get; set; }
                public Money(decimal amount) => Amount = amount;
                public override string ToString() => $"Rp {Amount}";
            }

            public static class Program
            {
                public static void Main()
                {
                    var price = new Money(1500);
                    Console.WriteLine(price.ToString());
                    Console.WriteLine($"Price: {price}");
                }
            }
            """);

        Assert.Equal("Rp 1500\nPrice: Rp 1500", output);
    }

    [Fact]
    public void InheritanceAndVirtualDispatchWork()
    {
        var output = Js.Run("""
            using System;

            public abstract class Shape
            {
                public abstract double Area();
                public virtual string Describe() => $"{Name()} with area {Area()}";
                protected virtual string Name() => "shape";
            }

            public class Square : Shape
            {
                private readonly double _side;
                public Square(double side) => _side = side;
                public override double Area() => _side * _side;
                protected override string Name() => "square";
            }

            public static class Program
            {
                public static void Main()
                {
                    Shape shape = new Square(4);
                    Console.WriteLine(shape.Area());
                    Console.WriteLine(shape.Describe());
                }
            }
            """);

        Assert.Equal("16\nsquare with area 16", output);
    }

    [Fact]
    public void MethodOverloadsResolveToTheRightBody()
    {
        var output = Js.Run("""
            using System;

            public static class Program
            {
                public static string Greet() => "hello";
                public static string Greet(string name) => $"hello {name}";
                public static string Greet(string name, int times) => $"hello {name} x{times}";

                public static void Main()
                {
                    Console.WriteLine(Greet());
                    Console.WriteLine(Greet("ana"));
                    Console.WriteLine(Greet("budi", 3));
                }
            }
            """);

        // JavaScript has no overloading, so each of these must reach a distinctly named method.
        Assert.Equal("hello\nhello ana\nhello budi x3", output);
    }

    [Fact]
    public void ConstructorOverloadsResolveCorrectly()
    {
        var output = Js.Run("""
            using System;

            public class Box
            {
                public int Width;
                public int Height;

                public Box(int size)
                {
                    Width = size;
                    Height = size;
                }

                public Box(int width, int height)
                {
                    Width = width;
                    Height = height;
                }
            }

            public static class Program
            {
                public static void Main()
                {
                    var square = new Box(5);
                    var rect = new Box(2, 3);
                    Console.WriteLine($"{square.Width}x{square.Height}");
                    Console.WriteLine($"{rect.Width}x{rect.Height}");
                }
            }
            """);

        Assert.Equal("5x5\n2x3", output);
    }

    [Fact]
    public void PatternMatchingSwitchExpressionWorks()
    {
        var output = Js.Run("""
            using System;

            public static class Program
            {
                private static string Classify(object value) => value switch
                {
                    int n when n < 0 => "negative int",
                    int n => $"int {n}",
                    string s => $"string of length {s.Length}",
                    null => "null",
                    _ => "something else",
                };

                public static void Main()
                {
                    Console.WriteLine(Classify(5));
                    Console.WriteLine(Classify(-3));
                    Console.WriteLine(Classify("halo"));
                    Console.WriteLine(Classify(null));
                    Console.WriteLine(Classify(true));
                }
            }
            """);

        Assert.Equal("int 5\nnegative int\nstring of length 4\nnull\nsomething else", output);
    }

    [Fact]
    public void IsPatternBindsVariable()
    {
        var output = Js.Run("""
            using System;

            public class Animal { }
            public class Dog : Animal { public string Bark() => "woof"; }

            public static class Program
            {
                public static void Main()
                {
                    Animal animal = new Dog();
                    if (animal is Dog dog)
                    {
                        Console.WriteLine(dog.Bark());
                    }

                    Animal other = new Animal();
                    Console.WriteLine(other is Dog);
                }
            }
            """);

        Assert.Equal("woof\nFalse", output);
    }

    [Fact]
    public void IteratorsBecomeGenerators()
    {
        var output = Js.Run(Js.InMain("""
            foreach (var value in Fibonacci(6))
            {
                Console.WriteLine(value);
            }
            """, """
                private static IEnumerable<int> Fibonacci(int count)
                {
                    int a = 0, b = 1;
                    for (var i = 0; i < count; i++)
                    {
                        yield return a;
                        var next = a + b;
                        a = b;
                        b = next;
                    }
                }
            """));

        Assert.Equal("0\n1\n1\n2\n3\n5", output);
    }

    [Fact]
    public async Task AsyncAwaitRoundTrips()
    {
        var output = Js.Run("""
            using System;
            using System.Threading.Tasks;

            public static class Program
            {
                private static async Task<int> DoubleAfterDelay(int value)
                {
                    await Task.Delay(1);
                    return value * 2;
                }

                public static async Task Main()
                {
                    var result = await DoubleAfterDelay(21);
                    Console.WriteLine(result);
                }
            }
            """);

        await Task.CompletedTask;
        Assert.Equal("42", output);
    }

    [Fact]
    public void TryCatchFinallyRunsInOrder()
    {
        var output = Js.Run(Js.InMain("""
            try
            {
                Console.WriteLine("try");
                throw new InvalidOperationException("boom");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"caught {ex.Message}");
            }
            finally
            {
                Console.WriteLine("finally");
            }
            """));

        Assert.Equal("try\ncaught boom\nfinally", output);
    }

    [Fact]
    public void UncaughtTypedExceptionKeepsPropagating()
    {
        var output = Js.Run(Js.InMain("""
            try
            {
                try
                {
                    throw new Exception("inner");
                }
                catch (FormatException)
                {
                    Console.WriteLine("wrong handler");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"outer caught {ex.Message}");
            }
            """));

        // A single JavaScript catch clause must not swallow exceptions the C# filter rejected.
        Assert.Equal("outer caught inner", output);
    }

    [Fact]
    public void StringMethodsMatchDotNet()
    {
        var output = Js.Run(Js.InMain("""
            var text = "  Halo Dunia  ";
            Console.WriteLine(text.Trim());
            Console.WriteLine(text.Trim().ToUpper());
            Console.WriteLine(text.Trim().Substring(5));
            Console.WriteLine(text.Trim().Substring(0, 4));
            Console.WriteLine(text.Contains("Dunia"));
            Console.WriteLine("a-b-c".Replace("-", "+"));
            Console.WriteLine(string.IsNullOrEmpty(""));
            Console.WriteLine("x".PadLeft(3, '0'));
            """));

        Assert.Equal("Halo Dunia\nHALO DUNIA\nDunia\nHalo\nTrue\na+b+c\nTrue\n00x", output);
    }

    [Fact]
    public void SubstringWithLengthUsesEndIndex()
    {
        // C#'s Substring(start, length) and JavaScript's substring(start, end) differ; a naive
        // mapping silently returns the wrong slice.
        var output = Js.Run(Js.InMain("""
            Console.WriteLine("abcdefgh".Substring(2, 3));
            """));

        Assert.Equal("cde", output);
    }

    /// <summary>
    /// A delegate stored in a member is invoked through that member, with or without an explicit
    /// <c>.Invoke</c>. The implicit form used to emit a call to the *object* holding the delegate
    /// — `job.Task()` became `job()` — which is a TypeError at runtime and invisible until then.
    /// </summary>
    [Fact]
    public void DelegateStoredInMemberIsInvokedThroughIt()
    {
        var output = Js.Run("""
            using System;
            using System.Collections.Generic;

            public sealed class Job
            {
                public Job(string name, Action work)
                {
                    Name = name;
                    Work = work;
                }

                public string Name { get; }
                public Action Work { get; }
                public Func<int, int> Double = n => n * 2;
            }

            public static class Program
            {
                public static void Main()
                {
                    var jobs = new List<Job>
                    {
                        new("first", () => Console.WriteLine("ran first")),
                        new("second", () => Console.WriteLine("ran second")),
                    };

                    foreach (var job in jobs)
                    {
                        // Implicit: the whole member access is the delegate.
                        job.Work();

                        // Explicit: names the delegate's own Invoke method.
                        job.Work.Invoke();

                        Console.WriteLine($"{job.Name}: {job.Double(21)}");
                    }
                }
            }
            """);

        Assert.Equal(
            "ran first\nran first\nfirst: 42\nran second\nran second\nsecond: 42",
            output);
    }

    [Fact]
    public void EventsInvokeSubscribers()
    {
        var output = Js.Run("""
            using System;

            public class Button
            {
                public event Action<string>? Clicked;
                public void Click(string label) => Clicked?.Invoke(label);
            }

            public static class Program
            {
                public static void Main()
                {
                    var button = new Button();
                    button.Clicked += label => Console.WriteLine($"first: {label}");
                    button.Clicked += label => Console.WriteLine($"second: {label}");
                    button.Click("ok");
                }
            }
            """);

        Assert.Equal("first: ok\nsecond: ok", output);
    }

    [Fact]
    public void StaticFieldsAndConstantsInitialise()
    {
        var output = Js.Run("""
            using System;

            public static class Config
            {
                public const string Name = "CSJS";
                public static int Counter = 10;
            }

            public static class Program
            {
                public static void Main()
                {
                    Console.WriteLine(Config.Name);
                    Config.Counter++;
                    Console.WriteLine(Config.Counter);
                }
            }
            """);

        Assert.Equal("CSJS\n11", output);
    }

    [Fact]
    public void NullConditionalAndCoalescingWork()
    {
        var output = Js.Run("""
            using System;

            public class Node { public string? Label; public Node? Next; }

            public static class Program
            {
                public static void Main()
                {
                    var node = new Node { Label = "root" };
                    Console.WriteLine(node.Next?.Label ?? "no next");
                    Console.WriteLine(node.Label ?? "unnamed");
                }
            }
            """);

        Assert.Equal("no next\nroot", output);
    }

    [Fact]
    public void ObjectInitializerAssignsProperties()
    {
        var output = Js.Run("""
            using System;

            public class User
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }

            public static class Program
            {
                public static void Main()
                {
                    var user = new User { Name = "Fadhil", Age = 30 };
                    Console.WriteLine($"{user.Name} is {user.Age}");
                }
            }
            """);

        Assert.Equal("Fadhil is 30", output);
    }

    [Fact]
    public void TuplesDestructure()
    {
        var output = Js.Run(Js.InMain("""
            var (name, age) = GetUser();
            Console.WriteLine($"{name} {age}");
            """, """
                private static (string, int) GetUser() => ("Ana", 28);
            """));

        Assert.Equal("Ana 28", output);
    }

    [Fact]
    public void PropertiesWithLogicRunTheirAccessors()
    {
        var output = Js.Run("""
            using System;

            public class Temperature
            {
                private double _celsius;

                public double Celsius
                {
                    get => _celsius;
                    set => _celsius = value;
                }

                public double Fahrenheit => _celsius * 9 / 5 + 32;
            }

            public static class Program
            {
                public static void Main()
                {
                    var temp = new Temperature();
                    temp.Celsius = 100;
                    Console.WriteLine(temp.Celsius);
                    Console.WriteLine(temp.Fahrenheit);
                }
            }
            """);

        Assert.Equal("100\n212", output);
    }

    [Fact]
    public void SwitchStatementBreaksBetweenSections()
    {
        var output = Js.Run(Js.InMain("""
            for (var i = 1; i <= 4; i++)
            {
                switch (i)
                {
                    case 1:
                        Console.WriteLine("one");
                        break;
                    case 2:
                    case 3:
                        Console.WriteLine("two or three");
                        break;
                    default:
                        Console.WriteLine("many");
                        break;
                }
            }
            """));

        // Missing breaks would fall through and print extra lines.
        Assert.Equal("one\ntwo or three\ntwo or three\nmany", output);
    }

    [Fact]
    public void NestedGenericsAndInterfacesWork()
    {
        var output = Js.Run("""
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public interface INamed { string Name { get; } }

            public class Product : INamed
            {
                public string Name { get; set; } = "";
                public double Price { get; set; }
            }

            public static class Program
            {
                public static void Main()
                {
                    var products = new List<Product>
                    {
                        new Product { Name = "Kopi", Price = 25000 },
                        new Product { Name = "Teh", Price = 15000 },
                        new Product { Name = "Susu", Price = 20000 },
                    };

                    var cheap = products.Where(p => p.Price < 22000)
                                        .OrderBy(p => p.Price)
                                        .Select(p => p.Name)
                                        .ToList();

                    Console.WriteLine(string.Join(", ", cheap));
                    Console.WriteLine(products.Sum(p => p.Price));
                }
            }
            """);

        Assert.Equal("Teh, Susu\n60000", output);
    }
}
