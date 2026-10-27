# Plan

The development roadmap for CSJS. Status lives in [Progress.md](Progress.md).

---

## Phase 1 — Foundation ✅

Everything in the specification, working end to end.

| | |
| --- | --- |
| Transpiler engine | Roslyn front end, ESNext emitter, `.d.ts` emitter, v3 source maps |
| Modern C# | Verified through C# 13: records, primary constructors, collection expressions, list patterns, raw strings, and the rest |
| Type safety layer | `TypeMap` shared by the emitter, the declaration writer, and IntelliSense |
| JS/TS interop | `[JsImport]`, `[JsGlobal]`, `[JsExport]`, `[JsName]`, `[JsBody]`, `[JsTemplate]`, `[JsObjectLiteral]`, `[JsIgnore]` |
| Optimised output | Tree-shakeable modules; helpers imported individually; no runtime shim layer |
| CLI | `cs2js` / `dotnet cs2js` — build, run, watch, serve, new |
| Templates | Blazor Hybrid, ASP.NET + JS, Console-to-Web |
| VS Code extension | Highlighting, IntelliSense, tasks, diagnostics, debugging bridge |
| Samples | Hello world, DOM manipulation, event handling, three.js |
| Testing framework | C# tests executed on a JavaScript runtime |
| Playground | The transpiler in the browser, via WebAssembly |
| CSJSGen | Avalonia + Blazor editor: Monaco, a file tree, resizable panels, a 21-template gallery, browser preview, and Jack The Code Bender |
| Distribution | `CSJS.Core`, `CSJS.Interop`, `CSJS.Cli`, `CSJS.Cli.Standalone`, `CSJS.Testing` — joined in Phase 2 by `CSJS.Build` and `CSJS.Templates` |

---

## Phase 2 — Depth ✅ (one item open)

Making the subset wider and the output better, in the order that removes the most friction.
Every item below was verified by executing it — most against the output of real .NET.

### Engine

- [x] **Struct value semantics.** Structs get `$clone()` and `$default()`; a struct read from
      storage is copied wherever C# copies it, `default(S)` and `new S[n]` are zeroed instances,
      and `Equals` compares fields. `readonly struct` is shared, since copying it is unobservable.
- [x] **Broaden the BCL map.** From 216 entries to 554, plus exact-signature keys so overloads
      that share an arity can map differently. `Regex` (with .NET group numbering), the whole LINQ
      surface (`Zip`, `Aggregate`, `SelectMany` overloads, `Chunk`, `MinBy`, `DistinctBy`, set
      operators…), `System.Text.Json` with real class instances on the way back, `TimeSpan`,
      `Stack`/`Queue`, `HashSet` set operations, `Enum` APIs, custom number and date formats.
      A test resolves every key against the real framework.
- [x] **`ref`/`out` on user methods.** Rewritten to extra tuple elements in the result, written
      back at the call site; the `.d.ts` declares the tuple.
- [x] **Full LINQ query syntax.** `join`, `join … into`, `let`, multiple `from`, multi-key
      `orderby`, `group … into`, and continuations, through transparent-identifier objects.
- [x] **`checked` arithmetic.** `checked` throws `OverflowException`, `unchecked` wraps, explicit
      narrowing casts wrap or throw, and `CheckForOverflowUnderflow` sets the default.
- [x] **C# 14.** The `field` keyword, extension members, null-conditional assignment, and
      first-class span conversions (`array.Contains` now binds to `MemoryExtensions`). Roslyn 5.0.
- [x] **Extension methods on binding types.** Worked already; now tested, and `x?.Ext()` — which
      emitted a call to a method the element does not have — is fixed along with every other
      `?.`-before-a-mapped-member case.
- [x] **Incremental compilation.** `TranspileSession` reuses references, the compilation, and
      every module whose text and the program's declared surface are unchanged; `cs2js build`
      skips an unchanged project entirely (~9 s → ~0.4 s on 41 files).

Found and fixed along the way, all silent before: `bool` printing `true`, enums concatenating as
numbers, char arithmetic yielding `NaN`, `Math.Round` rounding half up, `ToList()` aliasing,
LINQ `Reverse()` mutating its source, `ThenBy` replacing `OrderBy`, `FirstOrDefault` on value
types returning null, framework enum constants emitted as missing globals, unmapped framework
extension methods passing unchecked, dictionary index initialisers, modules in subfolders
importing `./X.js`, and abstract properties shadowing their overrides.

### Output quality

- [x] **Minification and bundling** — `cs2js build --bundle [--minify]`: one module, only the
      runtime helpers in use, and a merged source map that still points at the C#.
- [x] **Dead-code elimination** — unreferenced private members are left out unless their
      initialiser might have side effects.
- [x] **`const enum` style inlining** — a non-public enum never turned into text is written as
      numbers with no runtime object.

### Tooling

- [x] **Language Server Protocol.** `CSJS.LanguageService` gives Roslyn completion, hover,
      signature help, definition, and CSJS diagnostics; `cs2js lsp` serves it to VS Code (now a
      language client) and any LSP editor, and CSJSGen's Monaco calls it in-process.
- [x] **Hot reload** in `cs2js serve` — CSS swaps in place; a code change reloads the page; a
      failed build shows its errors over the page. True module hot-swapping is not attempted.
- [x] **Source-mapped stack traces** in `cs2js run`, via `node --enable-source-maps`.
- [x] **Rider and Visual Studio.** Through `CSJS.Build`: a CSJS project is an ordinary `.csproj`,
      so both IDEs give full IntelliSense, and CSJS diagnostics reach their error lists as
      MSBuild errors. `cs2js lsp` covers `.csjs` files through LSP4IJ or an LSP client extension.
      There is no dedicated plugin for either.

### CSJSGen

- [x] **Monaco editor**, giving C# colouring, folding, bracket matching, find and replace, and
      diagnostics in the margin — now with semantic completion from the language service.
- [x] **File tree explorer** with folders, expand and collapse, and file-type badges.
- [x] **Resizable panels**, by dragging the divider or with arrow keys.
- [x] **Project template gallery** — 21 starting points, each a working application.
- [x] **Browser preview** — builds the project, serves it, and opens the system browser.
- [x] **Assistant editing** — Jack changes the open file through `write`, `edit`, and `insert`,
      each of which compiles the result before reporting success.
- [x] **Persist panel widths and layout** to `app.config`.
- [x] **Native file dialogs** for open, save-as, and attachments.
- [x] **Tool-call transcript** in the chat — each step collapsible, with arguments and result.
- [ ] **Cross-platform.** Still open. The WinForms `BlazorWebView` host is Windows-only; Linux and
      macOS need a different WebView host (Photino, or Avalonia's own). Not attempted, because it
      cannot be run or verified on the machine this was built on — and an unverified port is
      worse than an honest gap.

### Distribution

- [x] **`CSJS.Build`** — MSBuild integration; no global tool needed.
- [x] **`CSJS.Templates`** — `dotnet new` pack: `csjs-app`, `csjs-lib`, `csjs-aspnet`,
      `csjs-blazor`, `csjs-console`, each built in CI from the packed artifacts.
- [x] **Package icon**, the VS Code extension packaged as a `.vsix` artifact, and publishing on a
      `v*` tag with the `NUGET_API_KEY` repository secret.

---

## Phase 3 — Ecosystem ✅

- [x] **Framework bindings** for React, Vue, and Angular, generated by `cs2js add` from their
      `.d.ts`, compiled, and run against the real packages. React reads close to idiomatic; Vue
      needs some explicit type arguments; Angular works but is rough — C# has no decorators, and
      Angular's ahead-of-time compiler cannot see C#.
- [x] **A binding generator** — `cs2js bind file.d.ts`, the inverse of the `.d.ts` emitter,
      reading the same type table through `TypeMap.FromTypeScript`.
- [x] **Package registry integration** — `cs2js add three` fetches the package, finds its
      typings (or `@types/…`), generates bindings, and pins the version in `csjs.json`.
- [x] **Playground sharing** via URL — the program travels in the fragment, so it is never
      uploaded.
- [x] **`CSJS.Web`** — every browser API as typed C#, generated from `@types/web` and shipped as
      a NuGet package.
- [x] **CSJS Gallery** — 21 browser-API demos and an API playground (REST, gRPC-Web, SignalR,
      Socket.IO, WebSocket, SSE), each demo beside its JavaScript at the seam.

### Next

- CSJSGen on Linux and macOS (above).
- `cs2js serve` injecting the import map that `cs2js add` prints, so bare imports work unbundled.
- Module hot-swapping that keeps page state, beyond reload-plus-CSS.
- Culture-aware formatting; number and date formats follow the invariant culture today.
- Divergences still documented rather than reproduced: integer division by zero yields
  `Infinity` instead of throwing, and arithmetic outside `checked`/`unchecked` is not wrapped.

---

## Phase 4 — Beyond JavaScript

From the specification's own roadmap.

- [ ] **WebAssembly output** as an alternative target. The engine keeps its emitter behind an
      interface for this reason; nothing in `TypeMap` assumes JavaScript specifically.
- [ ] **MAUI and IoT templates**, once a second emit target exists to make them worthwhile.

---

## Principles

Decisions that have already been made, recorded so they are not relitigated:

1. **Unmapped members are errors.** Emitting a call verbatim and letting the browser discover it
   does not exist is the worse failure mode.
2. **No runtime shim layer.** `List<T>` is an `Array`; `Dictionary<K,V>` is a `Map`. Wrapper
   types would make output unreadable and interop awkward.
3. **One type table.** The emitter, the `.d.ts` writer, and IntelliSense read the same source of
   truth, so they cannot disagree.
4. **Readable output.** Someone should be able to open the generated JavaScript and recognise
   their own code.
5. **The transpiler is the only authority.** Tooling asks it rather than reimplementing its
   judgement, so nothing drifts.
6. **Buy the editor, build the language.** Monaco handles text editing better than a hand-rolled
   surface ever would; the effort belongs in the parts that are actually CSJS.
7. **Verify by executing.** A feature is done when something ran it and the output matched — not
   when the code looks right. Every "verified" in `Progress.md` means exactly that.

---

Gravicode Studios — led by Kang Fadhil.
