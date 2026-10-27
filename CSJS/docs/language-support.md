# Language support

What transpiles, what it becomes, and what to do instead when it does not.

Every example here is taken from the test suite, so the emitted output is what the engine
actually produces.

---

## Types

| C# | JavaScript | TypeScript declaration |
| --- | --- | --- |
| `int`, `short`, `byte`, `float`, `double`, `decimal` | `number` | `number` |
| `long`, `ulong` | `BigInt` | `bigint` |
| `string` | `string` | `string` |
| `char` | one-character `string` | `string` |
| `bool` | `boolean` | `boolean` |
| `object` | any value | `any` |
| `T[]` | `Array` | `T[]` |
| `List<T>`, `IList<T>`, `IEnumerable<T>` | `Array` | `T[]` |
| `Dictionary<K,V>` | `Map` | `Map<K, V>` |
| `HashSet<T>` | `Set` | `Set<T>` |
| `Task`, `Task<T>` | `Promise` | `Promise<void>`, `Promise<T>` |
| `Func<…>`, `Action<…>` | function | arrow type |
| `DateTime` | `Date` | `Date` |
| `T?` (value type) | value or `null` | `T \| null` |
| `IGrouping<K,T>` | `Array` carrying a `key` | `T[] & { key: K }` |

`long` maps to `BigInt` because a 64-bit integer does not fit in a JavaScript `Number`. The
transpiler inserts `BigInt(…)` and `Number(…)` at the boundaries.

---

## Declarations

### Classes, inheritance, virtual dispatch

```csharp
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
```

Becomes a plain class hierarchy; `override` is just a method with the same name, and JavaScript's
own prototype chain does the dispatch.

### Records

Value equality is generated, and `==` routes to it:

```csharp
public record Point(int X, int Y);

var a = new Point(1, 2);
var b = new Point(1, 2);
Console.WriteLine(a == b);        // true
Console.WriteLine(a.ToString());  // Point { X = 1, Y = 2 }
```

Only the members you declared participate. The compiler-synthesised `EqualityContract` is
excluded, because comparing it would compare nothing useful.

### Enums

An enum emits a frozen object carrying both directions:

```javascript
export const Level = Object.freeze({
  Low: 1, Medium: 2, High: 3,
  1: 'Low', 2: 'Medium', 3: 'High',
});
```

The reverse map is what makes `$"{level}"` print `Medium` rather than `2`.

### Interfaces

Erased from JavaScript — they carry no runtime behaviour — but preserved in the `.d.ts`, which
is where their value is. Consequently `x is IFoo` cannot be tested at runtime and produces a
warning.

### Properties

An auto-property becomes a plain field. A property with a body keeps its accessors:

```csharp
public string Name { get; set; } = "";      // Name = '';
public double Area => Width * Height;       // get Area() { return … }
```

### Overloads

JavaScript has no overloading, so the first declaration keeps its name and the rest are
suffixed. Call sites are resolved through the semantic model, so each reaches the right body:

```csharp
public void Print() { }                          // Print()
public void Print(string text) { }               // Print$1(text)
public void Print(string text, int copies) { }   // Print$2(text, copies)
```

Constructors work the same way: the first is `constructor`, the rest become static factories,
and `new Box(2, 3)` emits `Box.new$1(2, 3)`.

### Events

An event is an array of handlers:

```csharp
public event Action<string>? Clicked;
Clicked?.Invoke(label);      // this.Clicked?.forEach($h => $h(label))
button.Clicked += Handle;    // .push(Handle)
button.Clicked -= Handle;    // csjsRemove(…)
```

---

## Statements and expressions

### Integer division

The difference that bites hardest, and the one CSJS handles everywhere:

```csharp
int a = 7, b = 2;
a / b        // Math.trunc(a / b)  →  3
-7 / 2       // -3, not -4
7.0 / 2      // 3.5, untouched
x /= 3;      // x = Math.trunc(x / 3)
```

### Pattern matching

```csharp
value switch
{
    int n when n < 0 => "negative int",
    int n => $"int {n}",
    string s => $"string of length {s.Length}",
    null => "null",
    _ => "something else",
}
```

Becomes a conditional chain. Pattern variables are hoisted to the top of the block, because C#
introduces them mid-expression and JavaScript has no syntax for that.

A switch expression with no discard arm throws when nothing matches, matching C#.

### Exceptions

Typed catch clauses work, which takes some machinery: every BCL exception would otherwise
collapse onto `Error` and become indistinguishable.

```csharp
try { … }
catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
catch (Exception) { … }
finally { … }
```

Exceptions are constructed carrying their C# type hierarchy, and clauses become a filter chain
that rethrows anything it did not match. Your own exception classes extend `Error`, so
`instanceof` handles them directly.

### Iterators

```csharp
public static IEnumerable<int> Squares(int count)
{
    for (var i = 1; i <= count; i++) yield return i * i;
}
```

Becomes a generator. The `.d.ts` declares `Generator<number, void, unknown>` rather than an
array, because that is what it actually is.

### async / await

`Task` is `Promise`, and `async`/`await` carry through unchanged. `Task.Delay` becomes a
promise-wrapped `setTimeout`; `Task.WhenAll` becomes `Promise.all`.

### LINQ

Method syntax maps onto array operations:

```csharp
numbers.Where(n => n % 2 == 0).OrderBy(n => n).ToList()
// numbers.filter(n => n % 2 === 0) → csjsOrderBy(…)
```

Because a `List<T>` is already an `Array`, chains do not accumulate spreads.

Query syntax is lowered onto the same operations, all of it: `from … from`, `where`, `let`,
`join`, `join … into`, `orderby` with several keys, `group … by`, and `into` continuations. Once
more than one range variable is in scope, each element becomes an object carrying all of them and
every lambda destructures it — the C# compiler's transparent identifiers, made visible:

```csharp
from p in people
join d in depts on p.Dept equals d.Id
let upper = p.Name.ToUpper()
orderby d.Title descending, upper
select $"{upper}@{d.Title}"
// csjsThenBy(csjsOrderBy(csjsInnerJoin(people, depts, (p) => p.Dept, (d) => d.Id,
//   (p, d) => ({ p, d })).map(({ p, d }) => ({ p, d, upper: p.Name.toUpperCase() })), …), …)
```

`ThenBy` refines the ordering `OrderBy` established instead of replacing it, join and group keys
compare with C# equality (so `new { a, b }` keys work), and `ToList()` always copies.

### `foreach` over a dictionary

`Map` iteration yields `[key, value]`, so `KeyValuePair.Key` and `.Value` map to index 0 and 1
and the loop stays a plain `for..of`:

```csharp
foreach (var entry in scores)
    Console.WriteLine($"{entry.Key} = {entry.Value}");
```

### `ref` and `out`

On BCL methods, the Try* pattern is rewritten into an assignment expression:

```csharp
if (scores.TryGetValue("ana", out var score)) { … }
// if (((score = scores.get('ana')) !== undefined)) { … }
```

On your own methods and local functions, by-reference parameters come back in the result: every
`return x` becomes `return [x, a, b]`, and the call site writes the extra elements back into the
variables it passed. That is exactly what the caller observes in C#.

```csharp
static bool TryHalve(int n, out int half) { … }
if (TryHalve(10, out var h)) …
// static TryHalve(n) { let half; … return [true, half]; }
// if ((($r0) => (h = $r0[1], $r0[0]))(Program.TryHalve(10))) …
```

The `.d.ts` says the same: `TryHalve(n: number): [boolean, number]`.

### Structs

A struct is a class whose copies are made explicitly. Each gets `$clone()` and a static
`$default()`, and a struct read from a variable, field, property, or element is cloned wherever
C# would copy it — assignment, argument passing, `return`, collection initialisers. `default(S)`,
`new S()` without a constructor of its own, an unassigned field, and every slot of `new S[n]` are
zeroed instances, never null. `Equals` compares field by field, as `ValueType.Equals` does.
A `readonly struct` cannot change, so it is shared rather than copied.

### Integer overflow and narrowing

`checked` throws `OverflowException` when a result leaves its type's range; `unchecked` wraps
(`int.MaxValue + 1` is `int.MinValue`, multiplication via `Math.imul`). An explicit narrowing cast
always wraps — `(byte)300` is 44 — or throws under `checked`. Outside either context, arithmetic
is written as it reads; `<CheckForOverflowUnderflow>` (or `TranspileOptions.CheckOverflow`)
makes `checked` the default, as it does for the C# compiler.

### `char`

A char is a one-character string, promoted to its UTF-16 code wherever C# promotes it:
`c - '0'` is `c.charCodeAt(0) - 48`, `(char)(c + 1)` is `String.fromCharCode(…)`.

### Conditional access

`a?.B` stays JavaScript's `?.` when `B` exists on the JavaScript object. When it does not — a
mapped BCL member, an extension method, a dictionary index — the receiver becomes a parameter, so
it is evaluated once and the result is C#'s `null`:

```csharp
list?.Where(x => x > 1).Count()
// (($r0) => $r0 == null ? null : $r0.filter((x) => x > 1).length)(list)
```

### C# 14

- `field` in an accessor names the property's synthesised storage.
- Extension members (`extension(string s) { … }`) become the static methods the compiler lowers
  them to; `"hi".Shout()` calls `StringExtensions.Shout('hi')` and an extension property reads
  through `get_IsBlank(s)`.
- Null-conditional assignment (`node?.Value = 5`) assigns only when the receiver is not null.
- First-class spans: `array.Contains(x)` binds to `MemoryExtensions` and is mapped accordingly.

### Text conversion

Wherever C# turns a value into text — interpolation, `+` with a string, `Console.WriteLine`,
`string.Join` — the C# rules apply: an enum prints its name (or `Light, Dim` for flags, or the
number when no member matches), a `bool` prints `True`, `null` prints nothing, and an object goes
through its `ToString()`.

```csharp
$"{value:F2}"     // csjsFormatValue(value, 'F2')
$"{level}"        // csjsEnumName(Level, level)
```

Standard and custom numeric formats (`N2`, `P1`, `X`, `0.00`, `#,##0`) and date formats
(`yyyy-MM-dd HH:mm`) follow the invariant culture. `Math.Round` rounds half to even.

### Framework coverage

Beyond strings, math, and collections: `Regex` (with .NET group numbering and substitutions),
`TimeSpan` (a number of milliseconds), `DateTime` arithmetic and formats, `System.Text.Json`
(`Deserialize<T>` builds real class instances, Maps, and Dates from a type description the
transpiler writes), `Stack<T>`/`Queue<T>`, the `HashSet<T>` set operations, the full LINQ surface
including `Zip`, `Aggregate`, `Chunk`, `MinBy`, `DistinctBy`, and the `Enum` APIs. Every key in
the mapping table is checked against the real framework by a test.

### Whole-program optimisation

Private members nothing references are left out (unless their initialiser might have side
effects), and a non-public enum that is never turned into text is inlined as numbers with no
runtime object — `3 /* Direction.West */`.

---

## Not supported

These are reported as errors rather than mistranslated.

### Pointers and `unsafe`

No equivalent exists.

### Unmapped BCL members

A framework member with no mapping is an error:

```
error CSJS0002: 'System.Globalization.CultureInfo.Name' has no JavaScript mapping and cannot be
transpiled. Use a supported member, or declare a binding with [JsGlobal] or [JsBody].
```

The alternative — emitting the call verbatim — would produce JavaScript that fails in the
browser with no indication of why. If you need a member that is not mapped, declare a binding:

```csharp
[JsGlobal("Intl")]
public static class Intl
{
    [JsBody("return new Intl.NumberFormat(locale).format(value);")]
    public static string FormatNumber(string locale, double value) => throw new JsExternException();
}
```

### `lock`

Compiles with warning `CSJS0006`. JavaScript is single-threaded, so the body runs but the lock
means nothing.

---

## Checking a specific feature

Two ways to find out whether something transpiles:

- The [Playground](../playground) — paste it and look at the output.
- CSJSGen — ask Jack, who runs `Csjs.check_support` and reports what the transpiler actually said.

---

Gravicode Studios — led by Kang Fadhil.
