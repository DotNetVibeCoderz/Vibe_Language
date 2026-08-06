# CSJS for Visual Studio Code

Language support for `.csjs` files — C# that transpiles to modern JavaScript.

## Features

**Syntax highlighting** built for CSJS rather than borrowed from C#. The interop attributes
(`[JsImport]`, `[JsExport]`, `[JsGlobal]`, `[JsBody]`, `[JsTemplate]`, `[JsIgnore]`,
`[JsObjectLiteral]`) get their own scope, because whether a declaration is a *binding* or a
*real type* is the thing you most need to see at a glance in this language.

**IntelliSense** for the CSJS layer: the interop attributes and the `CSJS.Browser` bindings,
with each completion showing what the member emits. Ordinary C# completion is left to the C#
extension, which does it better.

**Hover** over `List`, `Dictionary`, `Task`, or an interop attribute to see what it becomes in
JavaScript.

**Diagnostics** come from the transpiler itself via `cs2js`, so what you see in the Problems
panel is exactly what the build reports. There is deliberately no second analyser here — a
separate one would drift from the compiler and start flagging things the build does not.

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
