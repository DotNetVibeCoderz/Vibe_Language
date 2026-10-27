# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build CSJS.slnx                                    # everything; currently warning-free
dotnet test tests/CSJS.Core.Tests/CSJS.Core.Tests.csproj  # 213 tests
dotnet test tests/CSJS.Core.Tests/CSJS.Core.Tests.csproj --filter "FullyQualifiedName~IntegerDivisionTruncates"
dotnet run --project src/CSJSGen                          # the desktop editor (Windows only)
dotnet run --project playground                           # the browser playground
```

The CLI is installed as a global tool and both invocations work:

```bash
cs2js build samples/01-hello-world
dotnet cs2js build samples/01-hello-world
```

To reinstall after changing it:

```bash
dotnet pack src/CSJS.Cli src/CSJS.Cli.Standalone -c Release
dotnet tool update --global --add-source ./artifacts/nupkg CSJS.Cli
```

Node.js is required for `cs2js run`, the semantics tests, and `CSJS.Testing`.

To try the templates end to end, pack everything at one version into a local folder, install
`CSJS.Templates` from it, and build a project against that folder as a NuGet source — the
`templates` job in `.github/workflows/csjs-ci.yml` (at the Vibe_Language root) does exactly this.

## What CSJS is

A C# → JavaScript transpiler plus its ecosystem. `requirements.md` is the original spec (in
Bahasa Indonesia); `docs/architecture.md` describes what was actually built and why.

| Project | Role |
| --- | --- |
| `src/CSJS.Core` | The engine. Roslyn front end, JS emitter, `.d.ts` emitter, source maps. |
| `src/CSJS.Interop` | Interop attributes and browser bindings. Referenced by user code. |
| `src/CSJS.Cli` / `.Standalone` | `dotnet cs2js` and `cs2js`. Same sources, two tool command names. |
| `src/CSJS.Build` | MSBuild package: `dotnet build` transpiles a `.csproj`. Packs the CLI under `tools/`. |
| `src/CSJS.Templates` | `dotnet new` template pack built from `templates/`. |
| `src/CSJS.LanguageService` | Completion/hover/diagnostics, shared by `cs2js lsp` and CSJSGen. |
| `src/CSJS.Testing` | C# tests transpiled and run on Node. |
| `src/CSJSGen` | Avalonia + Blazor desktop editor with the Semantic Kernel assistant. |
| `playground` | `CSJS.Core` under WebAssembly. |
| `tests/CSJS.Core.Tests` | Emission tests and semantics tests. |

## Engine architecture

Read `docs/architecture.md` before changing the engine. The essentials:

**Two tables are the contract.** `Mapping/TypeMap.cs` says how C# types cross into JavaScript;
`Mapping/MemberMap.cs` says how BCL members do. Their consumers — the expression emitter, the
`.d.ts` emitter, the language service's hover, and the binding generator (`TypeMap.FromTypeScript`)
— read the same tables. Changing a mapping changes
all of them at once, which is the point: a `.d.ts` that disagrees with the emitted code is worse
than none.

**Unmapped framework members are errors, not guesses.** `MemberMap.RequiresMapping` decides
whether a symbol needs an entry; without one the emitter reports `CSJS0002` and the build fails.
This is deliberate — emitting a call verbatim produces JavaScript that fails in the browser with
no indication why. Do not "fix" a missing mapping by removing the check. Keys are
`Owner.Member/arity` or an exact `Owner.Member(type,type)` signature; `DepthTests` resolves every
key against the real framework, so a typo fails the build instead of never matching. Arity counts
optional parameters (`CountBy` is `/2`).

**Everything resolves through the semantic model.** Overload resolution, whether a division is
integral, whether a type is user-declared or BCL — none of these are decidable from syntax.

**The emitter is partial across four files.** `JsEmitter.cs` (declarations),
`JsEmitter.Statements.cs`, `JsEmitter.Expressions.cs`, and `JsEmitter.Lowering.cs` (struct copies,
query syntax, Enum/JSON APIs, C# 14 extension members). `Emit/ProgramAnalysis.cs` runs once over
every tree first — unused-private-member removal and enum inlining depend on it.

**Imports are collected during the walk and prepended afterwards**, so the source map must shift
down with them — that is what `SourceMapBuilder.PrependLines` is for. Getting this wrong is
silent: the map stays valid and every breakpoint lands a few lines off.

**No runtime shim layer.** `List<T>` is a real `Array`, `Dictionary<K,V>` a real `Map`.
`Runtime/csjs-runtime.js` holds only what has no faithful one-liner, and each helper is a
separate named export so bundlers drop the unused ones.

## Testing

Two kinds, catching different failures:

- **Emission tests** (`EmitTests.cs`) assert on generated text — helpers imported individually,
  no stacked spreads in LINQ chains, public types exported.
- **Semantics tests** (`SemanticsTests.cs`) transpile, run the result on Node, and compare
  against what C# prints. These catch a transpiler being plausible but wrong.
- **Parity tests** (`ParityTests.cs`) run whole programs whose expected output was captured from
  real .NET, not written by hand. Expectations written by hand once encoded `true` where C#
  prints `True`.

When adding engine behaviour, add a semantics test. `Js.Run(...)` in `TranspileHarness.cs` does
the transpile-and-execute round trip. Get expected output by running the same program on .NET
(`InvariantGlobalization`), never by reasoning about what C# "should" print.

## CSJSGen notes

Avalonia owns configuration and theming; the editor window is a WinForms `BlazorWindow` hosting
a `BlazorWebView`. It is **not** reparented into an Avalonia control — that hits the airspace
problem, where a native child window paints over composited content.

The editor is **Monaco** via `BlazorMonaco`, not a styled textarea. It brings its own
highlighting, gutter, folding, find/replace, and formatting, so edit commands trigger Monaco
actions rather than reimplementing them. Transpiler diagnostics are pushed into its margin as
markers.

Four things that will waste your time if you do not know them:

- `wwwroot` needs `<Content Update="wwwroot\**" CopyToOutputDirectory="PreserveNewest" />`.
  The Razor SDK otherwise routes it into static web assets and `HostPage` will not resolve.
- `index.html` must **not** use `autostart="false"` on `blazor.webview.js`, because nothing here
  calls `Blazor.start()`.
- `.monaco-host` must be `position: absolute; inset: 0` inside an `overflow: hidden` parent.
  Monaco measures its container, and a merely-flexed one settles on a width taken before the
  grid finished laying out — it then overflows into the next column.
- Panels subscribe to `ProjectService.Changed` themselves. Relying on a parent render to reach
  them leaves the explorer and properties panel stale when a project opens.
- Panel widths live in `AppState` and are rendered as explicit grid tracks with `Splitter`
  components between them. Drag tracking runs in JavaScript (`csjsgen.beginResize`) and reports
  only the resulting width — a pointermove per frame through interop lags behind the cursor.
- A `[JSInvokable]` reached through a `DotNetObjectReference` must be an **instance** method.
  A static one needs the assembly-qualified `DotNet.invokeMethodAsync` call instead, and fails
  silently otherwise — which is how the menu accelerators were dead without any error.

Kernel functions are the assistant's only extension point (`Kernel/*Plugin.cs`). Adding a
capability means adding a function; the chat loop never changes. AI settings live in
`app.config`, never hardcoded — and that file is gitignored because it holds real keys. The
committed example is `app.config.template`; keep the two in step when adding a setting.

Jack edits the open file through `write_editor`, `edit_editor`, and `insert_into_editor`. Each
compiles the result and reports the verdict, so a broken edit is caught while he can still fix
it. `Models/ProjectTemplates*.cs` holds the 21 scaffold templates; `TemplateTests` transpiles
every one, so a template that stops compiling fails the build.

**Browser preview** (`Services/PreviewServer.cs`) is why CSJSGen references `CSJS.Cli`: it runs
the CLI's own `BuildRunner`, so the preview builds a project exactly as `cs2js build` does,
assets included. Running on Node cannot show a page — Node has no DOM, so anything touching
`document` fails there by construction. That is the whole reason the button exists.

Reasoning models (o-series, gpt-5) reject `temperature`, `top_p`, and `max_tokens` outright —
`KernelFactory.IsReasoningModel` routes them to `max_completion_tokens` via `ExtraBody`. Sending
the ordinary settings to one is a 400, not a degraded answer.

## Playground notes

Two WebAssembly constraints, both already solved — do not undo them:

- `concurrentBuild: false` in `Transpiler.cs`. Roslyn's concurrent build waits on monitors,
  which WASM cannot do.
- `<WasmEnableWebcil>false</WasmEnableWebcil>`. Roslyn reads PE files; WebCIL wraps assemblies
  as WebAssembly modules. `ReferenceLoader` downloads them and passes them as
  `TranspileOptions.ReferenceImages`.

If the page hangs on "Memuat transpiler…" with *Failed to fetch dynamically imported module
dotnet.js*, the static-asset manifest points at a fingerprint that is no longer in the output —
usually after overlapping builds. Delete `playground/bin` and `playground/obj` and rebuild; it is
not a code problem.

## Conventions

- **Attribution.** "Gravicode Studios, led by Kang Fadhil" appears in generated file headers,
  the app's About dialog and status bar, and every doc footer. Keep it.
- **Bilingual docs.** `README.md` and `README.id.md` are kept in sync. In-app UI text is
  Indonesian; code comments and `docs/` are English.
- **Design system.** CSJSGen and the Playground share one: violet means source side, amber means
  emitted side, consistently. Monospace for chrome, sans for prose. The "seam" — the gradient
  rule between source and output — is the signature element. `frontend-design` skill is available
  and was used for it.
- **Tracking files.** `Plan.md` is the roadmap, `Progress.md` the status. Update them as work
  lands.

## Roadmap

`Plan.md` has the full list. Nearest items: broaden the BCL map, rewrite `ref`/`out` to tuples,
address struct value semantics, a language server for semantic IntelliSense, incremental
compilation.

WebAssembly output and MAUI/IoT templates are Phase 4 — do not build toward them, but avoid
designs that foreclose a second emit target.
