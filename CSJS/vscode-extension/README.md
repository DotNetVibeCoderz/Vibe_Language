# CSJS for Visual Studio Code

Language support for `.csjs` files — C# that transpiles to modern JavaScript.

## Features

**Syntax highlighting** built for CSJS rather than borrowed from C#. The interop attributes
(`[JsImport]`, `[JsExport]`, `[JsGlobal]`, `[JsBody]`, `[JsTemplate]`, `[JsIgnore]`,
`[JsObjectLiteral]`) get their own scope, because whether a declaration is a *binding* or a
*real type* is the thing you most need to see at a glance in this language.

**Real C# IntelliSense** from the CSJS language server (`cs2js lsp`): completion, hover,
signature help, and go-to-definition, resolved by Roslyn against the same references the
transpiler uses — so `Document.` completes the browser bindings and `string.` the BCL.

**Hover shows what a member emits**: `{this}.toUpperCase()` for `ToUpper`, or a warning when a
framework member has no JavaScript mapping.

**Diagnostics** are the compiler's plus the transpiler's own (`CSJS0002` and friends), pushed as
you type. They come from the transpiler itself, so the Problems panel shows exactly what the
build reports — there is deliberately no second analyser to drift from it.

`.csjs` files are served everywhere; `.cs` files only inside folders that contain a `csjs.json`
(a `.csproj` built with `CSJS.Build` is better served by the C# extension, which already sees it
as real C#). See [docs/language-server.md](../docs/language-server.md).

**Build tasks** (`csjs: build`, `run`, `watch`, `serve`) with a problem matcher.

**Side-by-side output** — `CSJS: Show generated JavaScript` opens the emitted module beside the
source. Reading the two together is the fastest way to understand a translation decision.

**Debugging bridge** — breakpoints in `.csjs` map onto the running JavaScript through the v3
source maps the transpiler emits. See [debugger-config.md](debugger-config.md).

## Requirements

The `cs2js` CLI must be on PATH:

```bash
dotnet tool install --global CSJS.Cli.Standalone   # gives you `cs2js`
dotnet tool install --global CSJS.Cli              # gives you `dotnet cs2js`
```

If you installed only `CSJS.Cli`, set `csjs.cliPath` to `dotnet cs2js`.

## Settings

| Setting | Default | What it does |
| --- | --- | --- |
| `csjs.cliPath` | `cs2js` | Path to the transpiler CLI. |
| `csjs.buildOnSave` | `false` | Transpile whenever a `.csjs` file is saved. |
| `csjs.showOutputOnBuild` | `true` | Reveal the output channel when a build reports errors. |

## Snippets

| Prefix | Produces |
| --- | --- |
| `csjsapp` | A browser entry point with `[JsExport(Global = true)]`. |
| `csjsimport` | A `[JsImport]` binding to a JavaScript module export. |
| `csjsglobal` | A `[JsGlobal]` binding to an existing global. |
| `csjsoptions` | A `[JsObjectLiteral]` options type. |
| `csjson` | A DOM event handler. |
| `csjsbody` | A method with a raw JavaScript body. |
| `csjstest` | A `[CsjsTest]` that runs on the JavaScript runtime. |

---

Gravicode Studios — led by Kang Fadhil.
