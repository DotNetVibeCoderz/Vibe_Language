# Progress

Development status. Roadmap lives in [Plan.md](Plan.md).

**Last updated:** 6 August 2026
**Version:** 0.1.0
**Build:** `dotnet build CSJS.slnx` — succeeds, no warnings
**Tests:** 120 passing, 0 failing

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
| `MemberMap` — BCL mappings | Done | 216 entries |
| Runtime helpers | Done | 37 tree-shakeable named exports |
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

## By the numbers

| | |
| --- | --- |
| Projects | 8 |
| Source files | 86 |
| Lines of code | ~20,100 |
| Tests | 120, all passing |
| BCL member mappings | 216 |
| Runtime helpers | 37 |
| Kernel functions | 18 |
| Project templates | 21 |
| Prompt templates | 24 |
| NuGet packages | 5 |

---

## Known limitations

Recorded honestly; each is on the roadmap.

**Engine**

- Structs are emitted as classes, so assignment shares rather than copies. This is a real
  divergence from C# and is not currently reported — the most significant known gap.
- `ref`/`out` on your own methods is rejected rather than rewritten. Return a tuple.
- `join`, `let`, and `group … into` in LINQ query syntax are unsupported. Method syntax works.
- `checked` arithmetic does not reproduce C# overflow behaviour.
- BCL coverage is broad but not complete. Anything missing is a build error, not silent
  breakage — but it is still something you have to work around.

**Tooling**

- The CLI recompiles everything on each build; there is no incremental path yet.
- The VS Code extension covers the CSJS layer only. Full C# IntelliSense needs the C# extension
  alongside it, and properly wants an LSP.
- `cs2js serve` has no hot reload.

**CSJSGen**

- Windows only — the BlazorWebView host is.
- File dialogs are typed paths rather than native pickers.
- Tool calls are not shown in the transcript, so Jack's compile and run steps are invisible even
  though they happen.
- Monaco gives colouring and structure but not semantic IntelliSense; that needs a language
  server, which is the LSP item on the roadmap.
- Panel widths reset between sessions — they live in memory rather than `app.config`.

---

## Next up

From [Plan.md](Plan.md), in the order that removes the most friction:

1. Broaden the BCL map — the most commonly hit limitation.
2. `ref`/`out` rewrite to tuples.
3. Struct value semantics, or a diagnostic if not.
4. A language server, so IntelliSense is semantic rather than syntactic.
5. Incremental compilation.

---

Gravicode Studios — led by Kang Fadhil.
