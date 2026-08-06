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
| Distribution | `CSJS.Core`, `CSJS.Interop`, `CSJS.Cli`, `CSJS.Cli.Standalone`, `CSJS.Testing` |

---

## Phase 2 — Depth

Making the subset wider and the output better, in the order that removes the most friction.

### Engine

- [ ] **Struct value semantics.** Structs are emitted as classes, so assignment shares rather
      than copies. This is the most significant divergence from C# still in the engine, and it is
      currently silent. Either rewrite assignment to copy, or report it — but not nothing.
- [ ] **Broaden the BCL map.** Every unmapped member is a build error today, which is honest but
      is also the most common thing to hit. `System.Text.RegularExpressions`, more of `System.Linq`
      (`Zip`, `Aggregate`, `SelectMany` overloads), `System.Text.Json`, and `TimeSpan` are the ones
      that come up first.
- [ ] **`ref`/`out` on user methods.** Currently rejected. A rewrite to a returned tuple with
      destructuring at the call site would make it work without lying about semantics.
- [ ] **Full LINQ query syntax.** `join`, `let`, and `group … into` are the gaps.
- [ ] **`checked` arithmetic.** C# overflow behaviour is not reproduced; either implement it or
      report it, but currently it silently differs.
- [ ] **C# 14 as it lands.** The `field` keyword, extension members, and null-conditional
      assignment are the ones that will matter; the audit harness in `ModernCSharpTests` is where
      they get pinned down.
- [ ] **Extension methods on binding types**, so people can extend `Element` the way they extend
      anything else.
- [ ] **Incremental compilation.** The CLI recompiles everything on each build. Fine for small
      projects, not for large ones.

### Output quality

- [ ] **Minification and bundling** as a `cs2js build --bundle` option, rather than expecting an
      external bundler.
- [ ] **Dead-code elimination** before emit, so unreferenced private members do not ship.
- [ ] **`const enum` style inlining** for enums that are never converted to string.

### Tooling

- [ ] **Language Server Protocol.** This is the one that unlocks the most: real C# IntelliSense
      with CSJS diagnostics inline, in VS Code *and* inside CSJSGen, from a single implementation.
      Both editors currently do their own syntactic approximation.
- [ ] **Hot reload** in `cs2js serve`, so a save updates the page without a refresh.
- [ ] **Source-mapped stack traces** in `cs2js run`, so a Node exception points at the C#.
- [ ] **Rider and Visual Studio** support.

### CSJSGen

- [x] **Monaco editor**, giving C# colouring, folding, bracket matching, find and replace, and
      diagnostics in the margin. Semantic completion still wants a language server.
- [x] **File tree explorer** with folders, expand and collapse, and file-type badges.
- [x] **Resizable panels**, by dragging the divider or with arrow keys.
- [x] **Project template gallery** — 21 starting points, each a working application.
- [x] **Browser preview** — builds the project, serves it, and opens the system browser, so a
      project that touches the DOM can be seen working rather than failing on Node.
- [x] **Assistant editing** — Jack changes the open file through `write`, `edit`, and `insert`,
      each of which compiles the result before reporting success.
- [ ] **Persist panel widths and layout** to `app.config`, so a session picks up where it left off.
- [ ] **Native file dialogs.** Paths are currently typed into a prompt.
- [ ] **Tool-call transcript** in the chat, so Jack's compile and run steps are visible rather
      than implied.
- [ ] **Cross-platform.** The BlazorWebView host is Windows-only today.

---

## Phase 3 — Ecosystem

- [ ] **Framework bindings** for React, Vue, and Angular, generated from their `.d.ts` rather
      than written by hand.
- [ ] **A binding generator** — point it at a `.d.ts` and get C# bindings out. This is the
      inverse of the `.d.ts` emitter and reads the same type table.
- [ ] **Package registry integration**, so `cs2js add three` fetches the library and generates
      bindings in one step.
- [ ] **Playground sharing** via URL, for bug reports and teaching.

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
