# Architecture

How CSJS is put together, and why. This is the document to read before changing the engine.

---

## The shape of a transpile

```
  .csjs source
       │
       ▼
  ┌─────────────┐   Roslyn parses and binds. Every emitter decision is made against the
  │   Roslyn    │   semantic model, never against syntax alone — overload resolution,
  │  front end  │   integer-ness, and user-type-versus-BCL are not decidable from syntax.
  └─────────────┘
       │  Compilation + SemanticModel
       ▼
  ┌─────────────┐
  │ NameResolver│   Assigns JavaScript names. Flattens namespaces, disambiguates only real
  └─────────────┘   collisions, and suffixes overloads after the first.
       │
       ├──────────────────────┬───────────────────────┐
       ▼                      ▼                       ▼
  ┌─────────┐          ┌────────────┐         ┌──────────────┐
  │JsEmitter│          │ DtsEmitter │         │SourceMapBuild│
  └─────────┘          └────────────┘         └──────────────┘
       │                      │                       │
       ▼                      ▼                       ▼
   Program.js            Program.d.ts          Program.js.map
```

All three read the same tables. That is the point of the next section.

---

## The two mapping tables

These are the heart of the engine, and the reason changes to them ripple predictably.

### `TypeMap` — how C# types cross into JavaScript

Three components consume it, and they must agree:

- **`JsEmitter`** calls `ShapeOf` to pick runtime operations. Integer division truncates only
  because `IsIntegral` says both operands are integral.
- **`DtsEmitter`** calls `ToTypeScript` to write declarations. A `Dictionary<K,V>` is declared as
  `Map<K,V>` because that is genuinely what the emitter produces.
- **The IntelliSense provider** in the VS Code extension shows what a member emits, using the
  same vocabulary.

Adding a type mapping in one place changes all three at once. That is deliberate: a `.d.ts` that
disagrees with the emitted code is worse than no `.d.ts` at all.

### `MemberMap` — how BCL members become JavaScript

A table of templates keyed by `Owner.Member/arity`:

```csharp
["System.String.Substring/2"] = new("{this}.substring({0}, {0} + {1})"),
["System.Linq.Enumerable.Where/1"] = new("{seq}.filter({0})"),
["System.Collections.Generic.List.Sort/0"] = new("csjsSort({this})", Helper: "csjsSort"),
```

Placeholders: `{this}` the receiver, `{seq}` the receiver as an array (a spread is inserted only
when it is not already one, so chained LINQ does not stack up `[...[...x]]`), `{0}`…`{n}` the
arguments, `{...}` all of them.

`Helper` names a runtime function. When a template uses one, the emitter adds a **named** import
for it — individually, so a bundler drops the helpers a program never calls.

**Anything absent from this table is an error, not a guess.** `MemberMap.RequiresMapping` decides
whether a symbol comes from the framework; if it does and has no entry, the emitter reports
`CSJS0002` and the build fails. This is the single most important design choice in the engine:
the alternative is emitting `CultureInfo.Name` verbatim and letting the browser discover it does
not exist.

---

## Where C# and JavaScript genuinely differ

The interesting parts of the emitter are the places the two languages disagree. Each of these is
a deliberate decision rather than an accident:

| C# | The problem | What CSJS does |
| --- | --- | --- |
| `7 / 2` | JavaScript `/` yields 3.5 | `Math.trunc(a / b)` when both sides are integral |
| `long` | exceeds `Number.MAX_SAFE_INTEGER` | maps to `BigInt`; conversions inserted at boundaries |
| overloads | JavaScript has none | first keeps its name, the rest get `$1`, `$2`; call sites resolve through the semantic model |
| constructors | JavaScript allows one | the first is `constructor`; the rest become static factories |
| `record` equality | `===` is reference equality | `csjsEquals`, plus a generated `Equals` |
| `enum` to string | it is a number at runtime | frozen object carries a reverse map; interpolation emits `Enum[value]` |
| typed `catch` | JavaScript has one catch clause | errors carry their C# hierarchy; clauses become a filter chain that rethrows what it did not match |
| `is Foo f` | no declaration inside an expression | binding hoisted to the block, pattern becomes an assignment |
| `out var` | same problem | same solution |
| `Dictionary` iteration | `Map` yields `[k, v]` pairs | `KeyValuePair.Key/.Value` map to index 0 and 1, so `foreach` is a plain `for..of` |
| `IGrouping<K,T>` | is both a sequence and a key | a real `Array` with `key` attached, so it iterates *and* has a key |
| interfaces | no runtime witness | erased from JavaScript, preserved in `.d.ts`; `is IFoo` warns |
| `null!` | a compile-time assertion | emitted as the operand alone |
| static ctor | C# runs it lazily | runs at module evaluation, which is deterministic |

---

## The runtime

`csjs-runtime.js` holds only what has no faithful one-liner in JavaScript. `List<T>` is a real
`Array` and `Dictionary<K,V>` is a real `Map` — no wrapper types — which is what keeps the output
readable and lets JavaScript callers use the results without ceremony.

Every helper is a separate named export. A module that needs `csjsSum` imports exactly that.

---

## Emission order and the source map

The emitter walks the body first and collects imports as it goes, because it cannot know which
helpers or modules are needed until it has seen the code. The import block is therefore
*prepended* afterwards — and the source map has to shift down with it, which is what
`SourceMapBuilder.PrependLines` is for.

Getting this wrong is silent: the map stays syntactically valid and every breakpoint lands a few
lines off.

---

## CSJSGen

```
  Avalonia          owns configuration, theming, and application identity
     │
     ▼
  BlazorWindow      a native frame hosting a BlazorWebView
     │
     ▼
  Blazor components the entire UI: editor, panels, chat
     │
     ├── ProjectService   project and file lifecycle, compile
     ├── ChatService      sessions, dispatch, persistence
     ├── KernelFactory    Semantic Kernel, provider selection, plugin registration
     └── EditorBridge     the seam that lets kernel functions read and write the editor
```

The WebView lives in its own native window rather than being reparented into an Avalonia control.
Reparenting hits the **airspace problem**: a native child window always paints over composited
content, so anything Avalonia drew beneath it would be invisible anyway.

Kernel functions are the assistant's only extension point. `CsjsPlugin` compiles and runs code,
`WebPlugin` reaches the internet, `UtilityPlugin` covers what models get wrong. Adding a
capability means adding a function — the chat loop never changes.

---

## The Playground

The same `CSJS.Core` assembly, running under WebAssembly. Two constraints shaped it:

- **Single-threaded.** Roslyn's concurrent build waits on monitors, which WASM cannot do, so the
  compilation is created with `concurrentBuild: false`.
- **No filesystem.** Reference assemblies cannot be loaded by path, so `TranspileOptions`
  accepts `ReferenceImages` as raw bytes and the Playground downloads them from `_framework/`.
  This also means WebCIL must be disabled, since Roslyn reads PE, not WebAssembly modules.

---

## Testing strategy

`tests/CSJS.Core.Tests` runs two kinds of check, and they catch different failures:

- **Emission tests** assert on the generated text — that helpers are imported individually, that
  a chained LINQ query does not stack spreads, that public types are exported.
- **Semantics tests** transpile, execute the result on Node, and compare the output against what
  C# would print. These are the ones that catch a transpiler being plausible but wrong.

Most behaviour worth trusting is covered by the second kind.
