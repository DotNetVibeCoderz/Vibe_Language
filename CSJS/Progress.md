# Progress

Development status. Roadmap lives in [Plan.md](Plan.md).

**Last updated:** 27 October 2026
**Version:** 0.3.0
**Build:** `dotnet build CSJS.slnx` — succeeds, no warnings
**Tests:** 229 passing, 0 failing

---

## Phase 1 — Foundation: complete

Everything the specification asks for is built, builds clean, and is verified by something other
than inspection. Where a line below says "verified", something was executed and its output
compared — not read and judged to look right.

### Transpiler engine — `src/CSJS.Core`

| Piece | State | Verified by |
| --- | --- | --- |
| Roslyn front end, diagnostics | Done | 5 diagnostic tests |
| JavaScript emitter | Done | 30 semantics tests, executed on Node |
| Modern C#, versions 9 through 13 | Done | 22 tests, executed on Node |
| `.d.ts` emitter | Done | 8 declaration tests |
| Source maps (v3, base64 VLQ) | Done | 3 source-map tests |
| `TypeMap` — shared type table | Done | read by all three emitters |
| `MemberMap` — BCL mappings | Done | 554 entries (Phase 2) |
| Runtime helpers | Done | 109 tree-shakeable named exports (Phase 2) |
| Multi-file, cross-module imports | Done | 3 multi-module tests |

The semantics tests are the ones that matter: each transpiles C#, runs the result on Node, and
compares the output against what C# itself would print. They cover integer division, enum-to-
string, records, overload resolution, iterators, async, typed catch clauses, pattern matching,
`out` parameters, LINQ, events, and the collection mappings.

**Modern C# reaches C# 13.** The audit found seven real gaps, all now closed: primary
constructors (captured parameters were emitted as nothing at all), `record struct` (extended
`ValueType`, which does not exist in JavaScript), lambda default parameters, named tuple
elements, `ImplicitElementAccess`, nested member initializers, and `IComparable.CompareTo`.

### Interop — `src/CSJS.Interop`

Attributes complete: `[JsGlobal]`, `[JsName]`, `[JsImport]`, `[JsExport]`, `[JsBody]`,
`[JsTemplate]`, `[JsObjectLiteral]`, `[JsIgnore]`.

Browser bindings cover `Document`, `Element`, `Node`, `Event`/`MouseEvent`/`KeyboardEvent`,
`Window`, `Storage`, `Location`, `Timers`, `Http`/`Response`, `Json`, `JsConsole`, `JsObject`.

Binding members camelCase automatically, so `[JsName]` is needed only where the JavaScript name
genuinely differs.

### CLI — `src/CSJS.Cli`, `src/CSJS.Cli.Standalone`

`build`, `run`, `watch`, `serve`, `new`, `version`. Both invocations work and are verified:

```
$ dotnet cs2js version
cs2js 0.1.0 — CSJS transpiler by Gravicode Studios
$ cs2js version
cs2js 0.1.0 - CSJS transpiler by Gravicode Studios
```

Packed and installed as .NET global tools from `artifacts/nupkg`. `CSJS.Testing` sits on the
CLI's reference set, so a scaffolded project can write tests without configuring anything.

### Testing framework — `src/CSJS.Testing`

`[CsjsTest]`, `[CsjsTestSuite]`, and an assertion library that is transpiled alongside the tests
so it exists in JavaScript. The harness is generated as C# rather than JavaScript, so it goes
through the same name mangling and overload resolution as the code under test.

### Samples — `samples/`

All four build. The three interactive ones were executed against a DOM stand-in with 15 behaviour
assertions, all passing.

| Sample | Shows |
| --- | --- |
| `01-hello-world` | A C# class called from HTML |
| `02-dom-manipulation` | Building and updating DOM nodes, a todo list |
| `03-event-handling` | A calculator: clicks, keyboard, C# events driving render |
| `04-threejs` | `[JsImport]` bindings, a 3D scene written in C# |

### Templates — `templates/`

All three build. The two server templates were run and verified serving.

| Template | State |
| --- | --- |
| `csjs-console-web` | Builds; output executed on Node, correct |
| `csjs-aspnet` | Builds; server runs, API and transpiled JS both served |
| `csjs-blazor-hybrid` | Builds; sparkline module verified with 11 assertions |

The MSBuild target transpiles before build, so `dotnet run` is all a user needs.

### VS Code extension — `vscode-extension/`

Grammar verified with `vscode-textmate` — the same tokenizer VS Code uses — across 15 scope
assertions, all passing. Interop attributes, browser bindings, BCL types, doc comments,
interpolated strings, and declarations all resolve to the intended scopes.

Also: IntelliSense for the CSJS layer, hover showing what a construct emits, diagnostics parsed
from `cs2js`, a `csjs` task type with a problem matcher, side-by-side generated output, and the
debugging bridge configuration.

### Playground — `playground/`

The transpiler running in the browser under WebAssembly. Verified end to end: transpiles the
Hello World sample in ~674 ms and produces correct output.

Two WebAssembly constraints had to be solved: Roslyn's concurrent build waits on monitors, so
compilation is serial; and reference assemblies cannot be read from disk, so they are downloaded
as PE images with WebCIL disabled.

### CSJSGen — `src/CSJSGen/`

Runs. Verified by screenshot and by driving the shipping services directly.

**Editor**

| Feature | State |
| --- | --- |
| Monaco: C# highlighting, folding, bracket matching, find/replace, formatting | Done |
| Custom Monaco themes matching the app palette, light and dark | Done |
| Line numbers, toggleable from the View menu | Done |
| Transpiler diagnostics shown in the editor margin | Done |
| File tree explorer: folders, expand/collapse, type badges | Done |
| Side panels hide, and resize by dragging or with arrow keys | Done |
| Keyboard accelerators — `F6` compile, `F5` run, `Shift+F5` browser, `Shift+F6` stop | Done, verified |
| Browser preview: builds, serves, opens the system browser | Done, verified |
| Stop: ends the Node process and the preview server | Done, verified |
| Native folder picker on open and new project | Done, verified |
| Run streams output live, so a background service is watchable | Done, verified |
| Project and file lifecycle (new/open/save/save-as/close) | Done |
| New-project gallery: 21 templates including blank | Done |
| Compile and run, with diagnostics and generated output | Done |
| Opening a project from the command line | Done |

**Jack The Code Bender**

| Feature | State |
| --- | --- |
| Multi-session: create, delete, reset, persisted to disk | Done |
| Edits the open file directly (write / edit / insert) | Done, verified against a live model |
| Image and document attachments | Done |
| Semantic Kernel: OpenAI, Azure OpenAI, Anthropic, Gemini, DeepSeek, Ollama | Done |
| Reasoning models (o-series, gpt-5) sent the parameters they accept | Done, verified |
| 18 kernel functions across the CSJS, web, and utility plugins | Done |
| Markdown rendering with tables, media, code blocks | Done |
| 24 prompt templates across 7 categories | Done |
| Dark and light themes, micro-interactions | Done |
| Settings persisted to `app.config` | Done |

**Verified against a live model.** Driving the shipping `ChatService` with Azure OpenAI
`gpt-5-mini`: 8 checks covering streaming, the system prompt, and three different kernel
functions that really transpile and execute code on Node — all passing. A second run covering
editor editing: 7 checks, all passing, with the resulting code compiling.

Two things that run found, both now fixed:

- Reasoning models reject `temperature`, `top_p`, and `max_tokens` outright with a 400 — not a
  degraded answer. `KernelFactory.IsReasoningModel` routes those to `max_completion_tokens`.
- Every menu accelerator was dead. `OnShortcut` was declared `static`, but JavaScript reached it
  through a `DotNetObjectReference`, which only calls instance members — and fails silently.

### Documentation — `docs/`

Bilingual README (English and Bahasa Indonesia), plus getting started, language support, interop
guide, CLI reference, CSJSGen guide, and architecture. Screenshots are current as of the Monaco
editor and the template gallery.

Attribution to Gravicode Studios and Kang Fadhil appears in the docs, the app's About dialog, the
status bar, and every generated file header.

---

## Phase 2 — Depth: complete except CSJSGen on Linux/macOS

### Engine — verified against .NET's own output

`ParityTests` runs six whole programs that were first run on .NET (invariant culture), with their
output captured verbatim; the transpiled programs must print exactly the same, and do. Between
them they compare 101 lines of output, covering every item below.

| Item | State | Verified by |
| --- | --- | --- |
| Struct value semantics (`$clone`, `$default`, field `Equals`) | Done | parity; `.d.ts` test |
| BCL map: 216 → 554 entries, exact-signature keys | Done | parity (54-line framework program); every key resolved against the real framework |
| `ref`/`out` on user methods and local functions | Done | parity; `.d.ts` tuple test |
| LINQ query syntax: `join`, `join … into`, `let`, `from … from`, `orderby a, b`, `group … into` | Done | parity |
| `checked`/`unchecked`, narrowing casts, `CheckForOverflowUnderflow` | Done | parity; option test |
| C# 14: `field`, extension members, null-conditional assignment, span conversions | Done | parity |
| `?.` before mapped members, extensions, and dictionary indexes | Done | parity |
| Extension methods on binding types | Done | emission test |
| Modules in subfolders import by relative path | Done | multi-folder program run on Node |
| Unused private member removal, enum inlining | Done | emission tests |
| Roslyn 4.14 → 5.0 | Done | full suite; Playground boots under WebAssembly |

Writing the parity tests also showed that six existing semantics tests had encoded a divergence
— they expected `true` where C# prints `True`. Their expectations are corrected; the lesson is now
in `CLAUDE.md`: take expected output from .NET, never from reasoning.

### Output and tooling

| Item | State | Verified by |
| --- | --- | --- |
| `cs2js build --bundle [--minify]` | Done | bundles of all four samples run; bundle output equals module output; maps decoded back to `.cs` lines |
| Incremental builds and watch | Done | no-op build ~9 s → ~0.4 s on 41 files; incremental output byte-identical to a full build |
| Live reload in `serve` (CSS hot-swapped, error overlay) | Done | event stream checked with curl while files were served |
| Source-mapped stack traces in `run` | Done | a throwing program prints `Helper.cs:12:9` |
| `CSJS.LanguageService` + `cs2js lsp` | Done | 12 service tests; the real `cs2js lsp` process driven over stdio in a test |
| VS Code language client | Done | packaged `.vsix` contains the client; not run inside VS Code itself |
| Rider and Visual Studio | Done, through `CSJS.Build` | a CSJS error appears as `App.cs(106,34): error CSJS0002` from `dotnet build` |

### CSJSGen

| Item | State | Verified by |
| --- | --- | --- |
| Layout persisted to `app.config` | Done | 36-check harness, including a real config round trip |
| Tool-call transcript in the chat | Done | fake-function harness; live Azure OpenAI `gpt-5-mini` run recorded `Csjs.read_editor` and `Csjs.compile` |
| Native dialogs: open file, save-as, attachments | Done | built; not clicked through interactively |
| Semantic completion, hover, live diagnostics in Monaco | Done | driven through WebView2 DevTools: `Document.` completions and a live `CSJS0002` marker |
| Linux and macOS | **Open** | not attempted — cannot be run here |

### Distribution

| Item | State | Verified by |
| --- | --- | --- |
| `CSJS.Build` | Done | template builds below; `dotnet run` served the app (HTTP 200 for page and module) |
| `CSJS.Templates` (5 templates) | Done | packed, installed in an isolated hive, a project created and built from each against the packed `CSJS.Build`; `csjs-lib`'s Node tests pass |
| Package icon | Done | in every `.nupkg` and the `.vsix` |
| CI: `vsix` and `templates` jobs; publish via `NUGET_API_KEY` | Done | workflow parsed; the same steps run locally |

## Phase 3 — Ecosystem: complete

| Item | State | Verified by |
| --- | --- | --- |
| Binding generator, `cs2js bind` | Done | 38 tests: generated C# compiled and transpiled, run on Node against JS matching the `.d.ts` |
| `cs2js add <npm-package>` | Done | run against react, vue, @angular/core, @angular/platform-browser |
| React, Vue, Angular bindings | Shipped in `bindings/` | components compiled and run against the real packages under jsdom |
| Playground share links | Done | a link encoded by an independent (Python) encoder opened and transpiled in the WebAssembly Playground; a link from the Share button decoded back byte-identically |
| `CSJS.Web` — the whole web platform as C# | Done | generated from `@types/web` (1,464 types, 19,338 members) by `cs2js bind --platform`; 4 binding tests; used by every gallery demo |
| CSJS Gallery (`gallery/`) | Done | 21 demos in six sections, each opened in headless Chrome with no page errors; network demos round-tripped against the gallery server (REST, gRPC-Web, SignalR, Socket.IO, WebSocket, SSE) |

The gallery found engine bugs that tests had not, each now fixed with a test whose expected output
came from .NET: method groups lost their receiver, nested types were not emitted, static
constructors leaked out as members, `x is A or B` compared against the constructor, a local could
shadow a browser global, `is null` missed `undefined`, local functions lost `this`, `{{`/`}}` came
out doubled, and a discard `_` became a real variable. CSJS.Build now re-transpiles when only the
engine changed.

---

## By the numbers

| | |
| --- | --- |
| Projects | 11 |
| Source files | 117 (plus 4 generated framework binding files) |
| Lines of code | ~39,100 (plus ~43,000 generated bindings) |
| Tests | 229, all passing |
| BCL member mappings | 554, plus generated `MathF` and integral-type entries |
| Runtime helpers | 109 |
| Kernel functions | 18 |
| Project templates | 21 in CSJSGen, 5 in `CSJS.Templates` |
| Prompt templates | 24 |
| NuGet packages | 8 |

---

## Known limitations

Recorded honestly; each is on the roadmap.

**Engine**

- Arithmetic outside `checked`/`unchecked` is not wrapped to 32 bits; doubles hold every result
  exactly, and wrapping every addition would bury the output. `unchecked` and
  `CheckForOverflowUnderflow` give the C# behaviour where it matters.
- Integer division by zero yields `Infinity` rather than throwing `DivideByZeroException`.
- Number and date formatting follow the invariant culture.
- BCL coverage is broad but not complete. Anything missing is a build error, not silent breakage.

**Tooling**

- Live reload reloads the page for code changes; only CSS is swapped in place.
- `cs2js serve` does not yet inject the import map `cs2js add` prints, so a page that imports an
  npm package by bare name needs the bundle or its own import map.
- The language server re-transpiles the whole project on each pause in typing.

**CSJSGen**

- Windows only — the BlazorWebView host is.

---

## Next up

From [Plan.md](Plan.md):

1. CSJSGen on Linux and macOS.
2. Import maps in `cs2js serve`.
3. Module hot-swapping that keeps page state.
4. Culture-aware formatting.

---

Gravicode Studios — led by Kang Fadhil.
