using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// Whole-program parity: each program below was run on .NET and its output captured verbatim;
/// the same program is transpiled, run on Node, and must print exactly that. A divergence
/// anywhere in a program — not just in the line a narrower test happens to check — fails here.
/// </summary>
public class ParityTests
{
    /// <summary>A raw string carries the source file's line endings; Node prints \n.</summary>
    private static string Normalize(string expected) => expected.ReplaceLineEndings("\n");

    /// <summary>Framework enum constants, bool text, char arithmetic, banker's rounding, ToList copying, LINQ Reverse not mutating, ThenBy as a secondary key, FirstOrDefault on value types.</summary>
    [Fact]
    public void CommonDivergencesMatchDotNet()
    {
        var output = Js.Run(""""
using System;
using System.Linq;
using System.Collections.Generic;
public static class Program {
  public static void Main() {
    var parts = "a,,b".Split(',', StringSplitOptions.RemoveEmptyEntries);
    Console.WriteLine(parts.Length);
    Console.WriteLine(true);
    Console.WriteLine($"{false}");
    char c = 'c'; Console.WriteLine(c - 'a');
    Console.WriteLine(Math.Round(2.5));
    var xs = new List<int> { 3, 1, 2 };
    var copy = xs.ToList(); copy.Add(9); Console.WriteLine(xs.Count);
    var ys = new[] { 1, 2, 3 }; var r = ys.Reverse().ToArray(); Console.WriteLine(ys[0]);
    var people = new[] { ("b", 2), ("a", 2), ("c", 1) };
    Console.WriteLine(string.Join(",", people.OrderBy(p => p.Item2).ThenBy(p => p.Item1).Select(p => p.Item1)));
    Console.WriteLine(new List<int>().FirstOrDefault());
    Console.WriteLine(DayOfWeek.Monday);
  }
}
"""");

        Assert.Equal(Normalize(""""
2
True
False
2
2
3
1
c,a,b
0
Monday
""""), output);
    }

    /// <summary>Struct assignment copies, default struct fields, enum concatenation, narrowing casts, unchecked wrap.</summary>
    [Fact]
    public void StructCopiesAndNarrowingMatchDotNet()
    {
        var output = Js.Run(""""
using System;
using CSJS.Browser;
public struct P { public int X; public int Y; public P(int x, int y) { X = x; Y = y; } }
public enum Color { Red, Green }
public static class ElExt { public static void Hide(this Element e) { e.SetAttribute("hidden", ""); } }
public static class Program {
  static P field;
  public static void Main() {
    var a = new P(1, 2); var b = a; b.X = 9; Console.WriteLine(a.X + " " + b.X);
    Console.WriteLine("c=" + Color.Green);
    int n300 = 300; Console.WriteLine((byte)n300);
    Console.WriteLine(field.X);
    int big = int.MaxValue; Console.WriteLine(unchecked(big + 1));
  }
  static void Use(Element e) { e.Hide(); }
}
"""");

        Assert.Equal(Normalize(""""
1 9
c=Green
44
0
-2147483648
""""), output);
    }

    /// <summary>C# 14 (field keyword, extension members, null-conditional assignment), ref/out on user methods, struct value semantics, full query syntax, checked arithmetic, char conversions.</summary>
    [Fact]
    public void DepthFeaturesMatchDotNet()
    {
        var output = Js.Run(""""
using System;
using System.Linq;
using System.Collections.Generic;

public class Temperature
{
    public double Celsius { get; set => field = Math.Max(-273.15, value); }
    public string Label { get => field ?? "none"; set; }
}

public class Node { public Node? Next; public int Value; }

public static class StringExtensions
{
    extension(string s)
    {
        public bool IsBlank => string.IsNullOrWhiteSpace(s);
        public string Shout() => s.ToUpper() + "!";
        public static string Twice(string x) => x + x;
    }
}

public struct Vec { public int X; public int Y; public void Move(int dx) { X += dx; } }

public static class Program
{
    static bool TryHalve(int n, out int half) { if (n % 2 != 0) { half = 0; return false; } half = n / 2; return true; }
    static void Swap(ref int a, ref int b) { (a, b) = (b, a); }
    static void Bump(ref Vec v) { v.X += 100; }

    public static void Main()
    {
        var t = new Temperature(); t.Celsius = -500; Console.WriteLine(t.Celsius); Console.WriteLine(t.Label);
        Node? n = null; n?.Value = 5; var m = new Node(); m?.Value = 7; Console.WriteLine(m.Value);
        Console.WriteLine("  ".IsBlank); Console.WriteLine("hi".Shout()); Console.WriteLine(string.Twice("ab"));
        if (TryHalve(10, out var h)) Console.WriteLine(h);
        Console.WriteLine(TryHalve(7, out _));
        int x = 1, y = 2; Swap(ref x, ref y); Console.WriteLine($"{x} {y}");
        var v = new Vec { X = 1 }; var w = v; w.Move(5); Console.WriteLine($"{v.X} {w.X}");
        Bump(ref v); Console.WriteLine(v.X);
        var list = new List<Vec> { v }; var first = list[0]; first.X = -1; Console.WriteLine(list[0].X);
        var arr = new Vec[2]; arr[0].X = 3; Console.WriteLine(arr[1].X + arr[0].X);
        Console.WriteLine(v.Equals(new Vec { X = 101 }));
        var people = new[] { new { Name = "Ana", Dept = 1 }, new { Name = "Budi", Dept = 2 }, new { Name = "Citra", Dept = 1 } };
        var depts = new[] { new { Id = 1, Title = "Eng" }, new { Id = 2, Title = "Ops" } };
        var q = from p in people
                join d in depts on p.Dept equals d.Id
                let upper = p.Name.ToUpper()
                orderby d.Title descending, upper
                select $"{upper}@{d.Title}";
        Console.WriteLine(string.Join(",", q));
        var g = from p in people group p.Name by p.Dept into grp select $"{grp.Key}:{string.Join("+", grp)}";
        Console.WriteLine(string.Join(" ", g));
        var pairs = from a in new[] { 1, 2 } from b in new[] { 10, 20 } where a * b > 10 select a + b;
        Console.WriteLine(string.Join(",", pairs));
        var gj = from d in depts join p in people on d.Id equals p.Dept into staff select $"{d.Title}={staff.Count()}";
        Console.WriteLine(string.Join(",", gj));
        int big = int.MaxValue;
        try { var z = checked(big + 1); Console.WriteLine(z); } catch (OverflowException) { Console.WriteLine("overflow"); }
        Console.WriteLine(unchecked(big * 2));
        char c = 'A'; Console.WriteLine((char)(c + 2)); Console.WriteLine((int)c);
    }
}
"""");

        Assert.Equal(Normalize(""""
-273.15
none
7
True
HI!
abab
5
False
2 1
1 6
101
101
3
True
BUDI@Ops,ANA@Eng,CITRA@Eng
1:Ana+Citra 2:Budi
21,12,22
Eng=2,Ops=1
overflow
-2
C
65
""""), output);
    }

    /// <summary>Regex, TimeSpan, DateTime formats, System.Text.Json, the wider LINQ surface, string and number formatting, Stack/Queue/HashSet, Enum APIs.</summary>
    [Fact]
    public void FrameworkBreadthMatchesDotNet()
    {
        var output = Js.Run(""""
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public enum Shade { Light = 1, Dark = 2, Dim = 4 }
public class Address { public string City { get; set; } = ""; }
public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public List<string> Tags { get; set; } = new();
    public Address? Home { get; set; }
    public string Display => $"{Name} ({Age})";
}

public static class Program
{
    public static void Main()
    {
        // Regex
        var re = new Regex(@"(?<word>\w+)@(\d+)");
        var m = re.Match("x alpha@12 beta@7");
        Console.WriteLine($"{m.Success} {m.Value} {m.Index} {m.Groups["word"].Value} {m.Groups[2].Value}");
        Console.WriteLine(string.Join("|", re.Matches("a@1 b@2 c@3").Select(x => x.Groups[1].Value)));
        Console.WriteLine(Regex.Replace("2024-01-05", @"(\d+)-(\d+)-(\d+)", "$3/$2/$1"));
        Console.WriteLine(Regex.Replace("hello world", @"\b\w", mm => mm.Value.ToUpper()));
        Console.WriteLine(Regex.IsMatch("ABC", "^abc$", RegexOptions.IgnoreCase));
        Console.WriteLine(string.Join(",", Regex.Split("a1b22c", @"\d+")));
        Console.WriteLine(m.NextMatch().Value);

        // TimeSpan & DateTime
        var span = TimeSpan.FromMinutes(90) + TimeSpan.FromSeconds(5);
        Console.WriteLine(span);
        Console.WriteLine($"{span.Hours} {span.Minutes} {span.TotalMinutes}");
        var d1 = new DateTime(2024, 3, 1, 8, 30, 0);
        var d2 = d1.AddDays(1.5);
        Console.WriteLine((d2 - d1).TotalHours);
        Console.WriteLine(d1.ToString("yyyy-MM-dd HH:mm"));
        Console.WriteLine(d1.AddMonths(-1).ToString("dd MMM yyyy"));
        Console.WriteLine(new DateTime(2024, 1, 31).AddMonths(1).Day);
        Console.WriteLine(d1.DayOfWeek);
        Console.WriteLine(DateTime.IsLeapYear(2024));

        // JSON
        var p = new Person { Name = "Ana", Age = 30, Tags = { "x", "y" }, Home = new Address { City = "Bandung" } };
        var json = JsonSerializer.Serialize(p);
        Console.WriteLine(json);
        var back = JsonSerializer.Deserialize<Person>(json)!;
        Console.WriteLine($"{back.Display} {back.Home!.City} {back.Tags.Count}");
        var camel = JsonSerializer.Serialize(new Address { City = "Solo" }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Console.WriteLine(camel);
        var dict = JsonSerializer.Deserialize<Dictionary<string, int>>("{\"a\":1,\"b\":2}")!;
        Console.WriteLine(dict["b"] + dict.Count);

        // LINQ breadth
        var nums = new[] { 5, 3, 8, 1, 9, 2 };
        Console.WriteLine(nums.Aggregate((a, b) => a * b));
        Console.WriteLine(nums.Aggregate(10, (acc, n) => acc + n));
        Console.WriteLine(string.Join(",", nums.Zip(new[] { "a", "b", "c" }, (n, s) => s + n)));
        Console.WriteLine(string.Join(",", nums.Chunk(4).Select(c => c.Sum())));
        Console.WriteLine($"{nums.Min()} {nums.Max(n => n * 2)} {nums.MinBy(n => -n)}");
        Console.WriteLine(string.Join(",", nums.TakeWhile(n => n > 2)) + ";" + string.Join(",", nums.SkipWhile(n => n > 2)));
        Console.WriteLine(string.Join(",", nums.Except(new[] { 3, 9 })) + ";" + string.Join(",", nums.Intersect(new[] { 1, 2, 7 })));
        Console.WriteLine(string.Join(",", new[] { "aa", "b", "cc", "d" }.DistinctBy(s => s.Length)));
        Console.WriteLine(string.Join(",", new[] { new[] { 1, 2 }, new[] { 3 } }.SelectMany(x => x)));
        Console.WriteLine(nums.SequenceEqual(new[] { 5, 3, 8, 1, 9, 2 }));
        Console.WriteLine(string.Join(",", nums.OrderDescending().Take(3)));
        Console.WriteLine(nums.ElementAt(2) + new List<int>().SingleOrDefault());
        Console.WriteLine(string.Join(",", nums.TakeLast(2)));
        Console.WriteLine(string.Join(",", new[] { "x", "yy", "z" }.GroupBy(s => s.Length, s => s.ToUpper()).Select(g => g.Key + "=" + string.Join("", g))));
        try { new int[0].First(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

        // Strings and numbers
        Console.WriteLine("Hello".Equals("hello", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("  -x-  ".Trim().Trim('-'));
        Console.WriteLine("abc".Insert(1, "Z") + "abcdef".Remove(2, 2));
        Console.WriteLine(new string('*', 3) + string.Concat(new[] { "a", "b" }));
        Console.WriteLine($"{Math.Round(2.5)} {Math.Round(3.5)} {Math.Round(2.345, 2)} {Math.Round(-2.5)}");
        Console.WriteLine($"{1234567.891:N2} {0.256:P1} {42:D5} {255:X} {3.14159:0.00} {1234:#,##0}");
        Console.WriteLine(Convert.ToInt32(2.5) + Convert.ToInt32("41"));
        Console.WriteLine(int.Parse("-17") + int.Parse(" 3 "));

        // Collections
        var stack = new Stack<int>(); stack.Push(1); stack.Push(2); stack.Push(3);
        Console.WriteLine(string.Join(",", stack) + " " + stack.Pop() + " " + stack.Peek());
        var queue = new Queue<string>(new[] { "a", "b" }); queue.Enqueue("c");
        Console.WriteLine(queue.Dequeue() + queue.Count);
        while (queue.TryDequeue(out var item)) Console.Write(item);
        Console.WriteLine();
        var set = new HashSet<int> { 1, 2, 3 }; set.UnionWith(new[] { 3, 4 }); set.ExceptWith(new[] { 1 });
        Console.WriteLine(string.Join(",", set) + " " + set.IsSubsetOf(new[] { 2, 3, 4, 5 }));
        var counts = new Dictionary<string, int>(); counts["a"] = 1;
        Console.WriteLine(counts.GetValueOrDefault("a") + counts.GetValueOrDefault("zz"));
        try { counts.Add("a", 2); } catch (ArgumentException) { Console.WriteLine("dup"); }
        var sb = new StringBuilder("ab"); sb.Insert(1, "-").Append('c', 2).Replace("b", "B");
        Console.WriteLine(sb.ToString());

        // Enums
        var shade = Shade.Light | Shade.Dim;
        Console.WriteLine(shade.HasFlag(Shade.Dim));
        Console.WriteLine(Enum.Parse<Shade>("Dark") + " " + string.Join(",", Enum.GetNames<Shade>()));
        Console.WriteLine(Enum.TryParse<Shade>("nope", out var bad) + " " + bad);
        Console.WriteLine(Shade.Dark.ToString().Length);
        Console.WriteLine((int)Enum.GetValues<Shade>().Last());
    }
}
"""");

        Assert.Equal(Normalize(""""
True alpha@12 2 alpha alpha
1|2|3
05/01/2024
Hello World
True
a,b,c
beta@7
01:30:05
1 30 90.08333333333333
36
2024-03-01 08:30
01 Feb 2024
29
Friday
True
{"Name":"Ana","Age":30,"Tags":["x","y"],"Home":{"City":"Bandung"},"Display":"Ana (30)"}
Ana (30) Bandung 2
{"city":"Solo"}
4
2160
38
a5,b3,c8
17,11
1 18 9
5,3,8;1,9,2
5,8,1,2;1,2
aa,b
1,2,3
True
9,8,5
8
9,2
1=XZ,2=YY
Sequence contains no elements
True
x
aZbcabef
***ab
2 4 2.35 -2
1,234,567.89 25.6 % 00042 FF 3.14 1,234
43
-14
3,2,1 3 2
a2
bc
2,3,4 True
1
dup
a-Bcc
True
Dark Light,Dark,Dim
False 0
4
4
""""), output);
    }

    /// <summary>`?.` followed by a mapped member, a LINQ call, an extension, or a dictionary index — none of which exist on the JavaScript object under the C# name.</summary>
    [Fact]
    public void NullConditionalOnMappedMembersMatchesDotNet()
    {
        var output = Js.Run(""""
using System;
using System.Linq;
using System.Collections.Generic;
public static class Program {
  public static void Main() {
    string? s = "abc"; string? n = null;
    List<int>? list = new() { 1, 2 }; List<int>? none = null;
    Dictionary<string, int>? d = new() { ["k"] = 7 };
    Console.WriteLine(s?.ToUpper() ?? "null");
    Console.WriteLine(n?.ToUpper() ?? "null");
    Console.WriteLine(list?.Count ?? -1);
    Console.WriteLine(none?.Count ?? -1);
    Console.WriteLine(list?.Where(x => x > 1).Count() ?? -1);
    Console.WriteLine(d?["k"] ?? -1);
    Console.WriteLine(s?.Length ?? -1);
  }
}
"""");

        Assert.Equal(Normalize(""""
ABC
null
2
-1
1
7
3
""""), output);
    }

    /// <summary>Abstract and virtual properties overridden in subclasses, ToString through Console.WriteLine, and GetType().Name.</summary>
    [Fact]
    public void AbstractAndVirtualPropertiesMatchDotNet()
    {
        var output = Js.Run(""""
using System;
using System.Collections.Generic;
public abstract class Shape { public abstract string Name { get; } public virtual int Sides { get; set; } = 0; public override string ToString() => $"{Name}:{Sides}:{GetType().Name}"; }
public class Square : Shape { public override string Name => "square"; public override int Sides { get => 4; set { } } }
public class Blob : Shape { public override string Name { get; } = "blob"; }
public static class Program {
  public static void Main() {
    var shapes = new List<Shape> { new Square(), new Blob() };
    foreach (var s in shapes) Console.WriteLine(s);
    Console.WriteLine(typeof(Square).Name);
  }
}
"""");

        Assert.Equal(Normalize(""""
square:4:Square
blob:0:Blob
Square
""""), output);
    }

    /// <summary>Template arguments that bind loosely (a conditional beside ??, a sum before .repeat), literal braces in templates, and StringBuilder.Append with an interpolated-string handler.</summary>
    [Fact]
    public void TemplateArgumentsKeepTheirMeaningMatchesDotNet()
    {
        var output = Js.Run(""""
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

public static class Program
{
    public static void Main()
    {
        var mapped = new List<int> { 3, 8, 12 };
        var start = 20;
        // A conditional argument next to ?? in the template.
        Console.WriteLine(mapped.FirstOrDefault(l => l > start, mapped.Count > 0 ? mapped[0] : 0));
        var none = new List<int>();
        Console.WriteLine(none.FirstOrDefault(l => l > 1, none.Count > 0 ? none[0] : -7));
        // A binary argument in front of a member call in the template.
        int a = 2, b = 3;
        Console.WriteLine(new string('*', a + b));
        Console.WriteLine("x".PadLeft(a * b, '.'));
        Console.WriteLine(string.Join("|", Enumerable.Repeat("ab", a + 1)));
        // Interpolated-string handler overloads.
        var sb = new StringBuilder();
        for (var i = 0; i < 3; i++) sb.Append($"[{i}:{i * i}]");
        sb.AppendLine($" done at {a + b}");
        Console.Write(sb.ToString());
        // A cast argument.
        Console.WriteLine(Math.Max((int)2.9, a));
        Console.WriteLine(TimeSpan.FromSeconds(a + b).TotalMilliseconds);
    }
}
"""");

        Assert.Equal(Normalize(""""
3
-7
*****
.....x
ab|ab|ab
[0:0][1:1][2:4] done at 5
2
5000
""""), output);
    }

    /// <summary>System.Text.Json over anonymous types, nested anonymous objects, lists of them, and an anonymous value in an object-typed property.</summary>
    [Fact]
    public void AnonymousTypesSerializeLikeDotNet()
    {
        var output = Js.Run(""""
using System;
using System.Text.Json;
using System.Collections.Generic;
public class Box { public string Label { get; set; } = ""; public object? Meta { get; set; } }
public static class Program
{
    public static void Main()
    {
        var title = "Buy milk";
        Console.WriteLine(JsonSerializer.Serialize(new { title }));
        Console.WriteLine(JsonSerializer.Serialize(new { Name = "Ana", Tags = new[] { "a", "b" }, Nested = new { Ok = true } }));
        Console.WriteLine(JsonSerializer.Serialize(new List<object> { new { X = 1 }, new { X = 2 } }));
        Console.WriteLine(JsonSerializer.Serialize(new Box { Label = "b", Meta = new { Id = 7 } }));
    }
}
"""");

        Assert.Equal(Normalize(""""
{"title":"Buy milk"}
{"Name":"Ana","Tags":["a","b"],"Nested":{"Ok":true}}
[{"X":1},{"X":2}]
{"Label":"b","Meta":{"Id":7}}
""""), output);
    }

    [Fact]
    public void DiscardsAreNotVariablesMatchesDotNet()
    {
        // `_` is a discard, not a binding: a later `var (_, b)` in the same function must not
        // turn the earlier `_ = Work();` into a temporal-dead-zone ReferenceError.
        var output = Js.Run(""""
using System;
using System.Threading.Tasks;
public static class Program
{
    static async Task Work() { await Task.Delay(1); Console.WriteLine("worked"); }
    static int Compute() => 42;
    public static async Task Main()
    {
        _ = Work();
        _ = Compute();
        var (_, b) = (1, 2);
        Console.WriteLine(b);
        if (int.TryParse("5", out _)) Console.WriteLine("parsed");
        var (x, (_, z)) = (3, (4, 5));
        Console.WriteLine(x + z);
        int c;
        (_, c) = (6, 7);
        Console.WriteLine(c);
        if (int.TryParse("nope", out var _)) Console.WriteLine("wrong"); else Console.WriteLine("rejected");
        await Task.Delay(20);
    }
}
"""");

        Assert.Equal(Normalize(""""
2
parsed
8
7
rejected
worked
""""), output);
    }
}
