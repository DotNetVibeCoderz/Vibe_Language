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
       ▼
  ┌─────────────┐   Whole-program facts no single file can establish: private members
  │ ProgramAnaly│   nothing references, and non-public enums never turned into text. Both
  │    sis      │   drive optimisations that are only sound with every tree in view.
  └─────────────┘
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

A table of templates keyed by `Owner.Member/arity`, or by an exact signature where arity is not
enough — `IndexOf(string, int)` and `IndexOf(string, StringComparison)` share an arity and mean
different things, so `System.String.IndexOf(string,System.StringComparison)` pins the second. The
exact signature is tried first:

```csharp
["System.String.Substring/2"] = new("{this}.substring({0}, {0} + {1})"),
["System.Linq.Enumerable.Where/1"] = new("{seq}.filter({0})"),
["System.Collections.Generic.List.Sort/0"] = new("csjsSort({this})", Helper: "csjsSort"),
```

Placeholders: `{this}` the receiver, `{seq}` the receiver as an array (a spread is inserted only
when it is not already one, so chained LINQ does not stack up `[...[...x]]`), `{copy}` the receiver
as a *new* array (`ToList()` never aliases in C#), `{default}` the C# default of the result type
(`FirstOrDefault` on an empty `List<int>` is 0, not null), `{0}`…`{n}` the arguments, `{...}` all
of them.

Every `csjs*` function a pattern calls gets a **named** import — individually, so a bundler drops
the helpers a program never calls. `StringifyArguments` marks members like `Console.WriteLine`
whose arguments C# converts to text by its own rules.

Because a mistyped key fails silently — it simply never matches — `DepthTests` resolves every key
against the real framework, and checks that every helper a pattern calls is exported by the
runtime. Both tests found real dead keys the day they were written.

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
| `struct` assignment | copies; JavaScript shares | `$clone()` wherever C# copies; `$default()` for `default(S)` |
| `ref` / `out` | no by-reference parameters | extra tuple elements in the result, written back at the call site |
| `checked` / casts | JavaScript never overflows | `csjsChecked` throws; `unchecked` and narrowing casts wrap (bitwise or, `Math.imul`) |
| `char` | a one-character string | promoted to `charCodeAt(0)` where C# promotes it to a number |
| text conversion | `true`, enum ordinals, `null` | `True`, member names, nothing — at interpolation, `+`, and `Console` |
| `a?.Mapped()` | no such member on the object | receiver becomes a lambda parameter; the member is applied to it |
| declaration order | class bindings have a TDZ | enums and structs first, base classes before subclasses |
| subfolders | `./X.js` only works side by side | imports are paths relative to the importing module |

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

## Incremental compilation

`TranspileSession` (`TranspileSession.cs`) is a transpiler that remembers. `watch` and `serve`
keep one alive; the CLI's one-shot `build` relies on the cache stamp instead (see
[CLI](cli.md#incremental-builds)), because a new process has nothing to remember.

It is a layer over the one pipeline, not a second pipeline. `Transpiler` exposes an internal
`TranspileHooks` seam with three optional callbacks, and does exactly what it always did when
they are absent:

```
  Transpile(documents)
       │  references ◄───────── Hooks.References     the framework set, loaded once per session
       ▼
  CSharpCompilation.Create
       │  compilation ◄──────── Hooks.Compilation    previous compilation + ReplaceSyntaxTree
       ▼                                             for the files whose text changed
  diagnostics, NameResolver, ProgramAnalysis         always over the whole program
       │
       ▼  for each file
  EmitModule ◄────────────────── Hooks.ReuseModule   the previous module, when provably unchanged
```

**When reuse is sound.** A module's JavaScript depends on more than its own text: on the names
the `NameResolver` gives other files' types and overloads, on which file declares which type,
and on the whole-program `ProgramAnalysis`. So a module is reused only when all three hold:

1. its own text is identical;
2. the **declared surface** of the whole program is identical — every file's tokens, trivia
   dropped, with method and accessor bodies replaced by a marker that keeps only whether a body
   exists (an auto-property and a computed one emit differently). Field initialisers, constants,
   attributes, usings, and default parameter values all stay in. Partial types stay in *whole*,
   bodies included, because their parts are emitted from different files;
3. the analysis facts that apply to it are identical: the set of inlined enums (a body in one
   file that interpolates an enum stops it being inlined in another) and the unused private
   members it declares.

Anything else re-emits — the whole program, when the surface changed. The rule errs towards
rebuilding: the cost of being wrong is a slower rebuild, never stale output. The tests check every
incremental result against a cold transpile of the same sources, byte for byte.

Diagnostics are never cached from Roslyn: the whole compilation is checked on every run, and the
emitter's own diagnostics for a reused module are replayed from when it was emitted. A failed
build caches nothing, so the modules of the last good build stay valid for when the error is
fixed.

If `ProgramAnalysis` gains a new body-dependent fact, it has to join the reuse key in
`TranspileSession` — otherwise a body edit in one file could leave another file's module stale.

---

## Bundling

`Bundling/` turns the modules of one transpile into a single ES module. It can be a rearranging
step rather than a real bundler because the emitter's output is regular: every top-level name is
already unique across the program, imports between modules name exactly what they bind, and the
runtime is a flat list of declarations.

- **`JsTokenizer`** is just precise enough to rewrite JavaScript safely: strings, template
  literals with nested `${}` (tokenized recursively to find their end), regex literals (decided
  from the previous significant token, as every hand-written lexer does), and comments. It also
  records whether a line break preceded each token, which is what automatic semicolon insertion
  keys on.
- **`Bundler`** parses each module's top-level imports and declarations, orders modules as ES
  module evaluation would (depth first, imports in written order), blanks out local imports,
  hoists and merges the external ones, and appends each module line for line. Because lines are
  carried whole, each module's map is copied across with only a line offset — the merged map is a
  plain v3 map, not an index map with `sections`, which every consumer understands.
- **`RuntimeLibrary`** splits `csjs-runtime.js` into its top-level declarations, exported or not,
  and records which other declarations each one mentions. A bundle carries the closure of what
  the modules reference, with `export` removed. A bare top-level statement in the runtime is
  rejected rather than guessed about, since dropping it could drop a side effect.
- **`JsMinifier`** removes comments, indentation, and unnecessary spaces, keeping one line break
  wherever the source had one. It records where every token moved, and the bundler rewrites the
  merged map through that table, so a minified bundle still maps to the C# line.
- **`SourceMapDocument`** decodes v3 maps, for merging and for the tests that check a bundle line
  really lands on the C# line it claims.

The regex-or-division heuristic treats a `/` after `)` as division. The emitter never writes a
regex literal there; hand-written JavaScript in a `[JsBody]` that does would need parentheses.

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

## Lowering — `JsEmitter.Lowering.cs`

The emitter is partial across four files: declarations, statements, expressions, and lowering.
The fourth holds the constructs that need a rewrite rather than a translation: struct copies,
LINQ query syntax with transparent identifiers, the `Enum` and `JsonSerializer` APIs (which need a
type's names or shape at runtime, written out at compile time), the C# 14 `field` keyword,
extension members, and null-conditional assignment.

A rewritten `a?.M()` needs its receiver as a parameter. Rather than thread a second code path
through every template and extension call, `EmitContext.CreateStandIn` makes a synthetic
identifier that the emitter writes as the parameter name, while `TypeFor` and `SymbolFor` answer
for the expression it replaced — so every existing path that takes a receiver works unchanged.

---

## MSBuild integration — `CSJS.Build`

A `.csproj` that references `CSJS.Build` transpiles after `CoreCompile`. The package carries the
published CLI under `tools/` and runs `cs2js compile` out of process: Visual Studio hosts MSBuild
on .NET Framework with its own Roslyn, and CSJS needs .NET 10 and Roslyn 5. Diagnostics are
printed in MSBuild's canonical form with absolute paths, which is the whole of the IDE
integration — the error list, the squiggles, and go-to-error come from the IDE's existing
handling of build output. IntelliSense comes from the project being real C#.

---

## The Playground

The same `CSJS.Core` assembly, running under WebAssembly. Two constraints shaped it:

- **Single-threaded.** Roslyn's concurrent build waits on monitors, which WASM cannot do, so the
  compilation is created with `concurrentBuild: false`.
- **No filesystem.** Reference assemblies cannot be loaded by path, so `TranspileOptions`
  accepts `ReferenceImages` as raw bytes and the Playground downloads them from `_framework/`.
  This also means WebCIL must be disabled, since Roslyn reads PE, not WebAssembly modules.

Share links put the program in the URL fragment, deflated and base64url-encoded. A fragment is
never sent to a server, so sharing a link never uploads the code.

---

## Testing strategy

`tests/CSJS.Core.Tests` runs two kinds of check, and they catch different failures:

- **Emission tests** assert on the generated text — that helpers are imported individually, that
  a chained LINQ query does not stack spreads, that public types are exported.
- **Semantics tests** transpile, execute the result on Node, and compare the output against what
  C# would print. These are the ones that catch a transpiler being plausible but wrong.

Most behaviour worth trusting is covered by the second kind. `ParityTests` takes it furthest:
whole programs run on .NET, their output captured verbatim, and the transpiled program must print
exactly the same — a divergence anywhere in the program fails, not just in the line a narrower
test checks.
