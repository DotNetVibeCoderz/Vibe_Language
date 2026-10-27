# CLI reference

```
cs2js <command> [directory] [options]
```

Both invocations work if you installed both packages:

```bash
cs2js build
dotnet cs2js build
```

---

## `cs2js new <name>`

Scaffolds a project: `csjs.json`, `src/Program.csjs`, `index.html`, and a `.gitignore`.

```bash
cs2js new HelloWorld
```

## `cs2js build [directory]`

Transpiles the project. Directory defaults to the current one.

```bash
cs2js build
cs2js build ./client
```

Writes to the configured output directory:

- `Name.js` — the module
- `Name.d.ts` — TypeScript declarations, unless disabled
- `Name.js.map` — the source map, unless disabled
- `csjs-runtime.js` — only when a module actually imported a helper
- every other file in the project — stylesheets, images, media, fonts, data, `.env`, text —
  copied with its folder structure intact, so the output is a directory you can serve as-is

The C# sources are not copied. Each map embeds `sourcesContent`, so a debugger already has
the original text — shipping `.csjs` beside the JavaScript would publish your source without
buying anything.

Assets keep their path relative to the project, except those beside the sources, which stay
relative to the source root so they land next to the module that references them. Unchanged
files are skipped, so watch mode does not rewrite the tree on every keystroke. Set
`"assets": false` in `csjs.json` to emit JavaScript only.

```bash
cs2js build --entry      # append the call to Main()
```

`--entry` matters when something other than `cs2js` loads the emitted module — Electron reads
`main` from `package.json` and runs it directly, so nothing else is there to start the program.

Exits non-zero when the build reports errors, so it composes with CI and MSBuild targets.

Each `.js.map` sets `sourceRoot` to the path from the map back to the source directory, so a
stack trace or browser devtools names `src/Program.csjs` rather than a file beside the output
that does not exist.

### Incremental builds

A build that would change nothing does nothing. After a successful build, a stamp is written to
`obj/csjs/build.stamp.json`, keyed on everything that decides the output: the `cs2js` and engine
builds themselves, `csjs.json`, the command-line switches, each source's content, and the extra
references and assets by size and timestamp. The next build compares keys first and, if they
match and every output the stamp lists is still on disk as that build left it, prints
`Up to date` and stops. Deleting a file from `dist` is enough to rebuild.

When a build does run, files are only rewritten when their content changed, so an edit to one
`.cs` file touches only its own `.js` and `.js.map`, and downstream watchers stay quiet.

```bash
cs2js build --force          # ignore the stamp and the session: a full, cold build
cs2js build --no-incremental # the same
```

`watch` and `serve` go further and keep a transpile session alive between rebuilds (see
[Architecture](architecture.md#incremental-compilation)): the framework references are loaded
once, the compilation is updated rather than recreated, and only modules whose source changed are
re-emitted — unless the change altered a declaration other files depend on, in which case
everything is. Measured on Windows with a Debug build of the CLI; "before" is `--force`, which
is exactly the old behaviour:

| Scenario | Before | After |
| --- | --- | --- |
| `build`, nothing changed, 4 files | ~2,700 ms | ~350 ms wall, ~60 ms of it work (`Up to date`) |
| `build`, nothing changed, 41 files | ~9,000 ms | ~370 ms wall, ~70 ms of it work |
| `build`, one file edited | unchanged: a new process pays the cold start either way | |
| `watch`, one method body edited, 4 files | 200–500 ms | 80–470 ms (1 emitted, 3 reused) |
| `watch`, one method body edited, 41 files | 3.1–3.3 s | ~2.6 s (1 emitted, 40 reused) |

The watch gain is modest on purpose: C# diagnostics and the whole-program analysis still run over
every file on every rebuild, because skipping them is where an incremental compiler starts
producing stale answers. What is cached is the reference set, the compilation's declaration
tables, and emission.

### Bundling

```bash
cs2js build --bundle     # dist/bundle.js + bundle.js.map
cs2js build --minify     # the same, minified (implies --bundle)
cs2js run --bundle       # run the bundle instead of the modules
```

`--bundle` combines the emitted modules into one ES module, with no external bundler:

- Modules are ordered as ES modules evaluate them — dependencies first, imports in the order
  written — and the imports between them are dropped.
- Imports of anything else (`three`, a URL, a hand-written `.js` beside the sources) are kept,
  merged per specifier, and hoisted to the top. A hand-written file's path is rewritten to be
  relative to the bundle.
- Only the runtime helpers the program uses are included, with whatever they call in turn —
  including the runtime's internal, non-exported functions and tables. They are not exported.
- **Every export of every module is kept.** These are the public types, the same names a page
  imported from the individual modules; import them from `bundle.js` instead.
- Two top-level declarations with the same name are an error, not a silent merge.

The bundle has a merged source map that leads back to the `.cs` lines, so stack traces and
devtools work exactly as they do for the modules.

`--minify` strips comments, indentation, blank lines, and every space that is not needed between
tokens. It renames nothing and keeps one line break between statements wherever the source had
one, because automatic semicolon insertion makes those meaningful; it is safe on anything the
tokenizer understands (strings, template literals with nested `${}`, regex literals), and the
source map is rewritten to the new positions, so it stays exact. The size win comes from the
indentation and comments — expect roughly a third off, not the two thirds a renaming minifier
gets.

A bundle build writes `bundle.js` and `bundle.js.map` (and the assets) instead of the per-module
files, `.d.ts` included. Settings live in an optional `bundle` section of `csjs.json`:

```json
{
  "bundle": { "enabled": true, "file": "app.js", "minify": true, "modules": false }
}
```

`enabled` bundles on every build without `--bundle`; `modules: true` writes the individual
modules and their declarations beside the bundle as well.

## `cs2js run [directory | file]`

Builds, then executes with Node. The entry point is the first of `Program.js`, `Main.js`,
`main.js`, or `index.js` found in the output.

```bash
cs2js run                # the project
cs2js run Scratch.csjs   # a single file, transpiled to a temporary directory
```

Requires a class with a static `Main`.

Both forms write source maps and start Node with `--enable-source-maps`, so an exception that
escapes points at the C#:

```
InvalidOperationException: kaboom from C#
    at Helper.Explode (C:\work\app\src\Helper.cs:12:9)
    at Program.Main (C:\work\app\src\Program.cs:8:9)
```

The innermost frame of a C# exception is the runtime's `csjsError`, which builds it.

## `cs2js watch [directory]`

Rebuilds whenever a `.cs`, `.csjs`, or `.json` file changes. Writes are debounced by 150 ms,
because editors save in several steps and rebuilding against a half-written file is noise.
Rebuilds are incremental; each prints how many modules were emitted and how many reused.
`--bundle`, `--minify`, and `--force` apply as they do to `build`.

## `cs2js serve [directory]`

Builds, then serves the output over HTTP with live reload.

```bash
cs2js serve --port 8080
cs2js serve --no-reload   # a plain static server, as before
```

The `html` entry in `csjs.json` is copied in as `index.html`. Without it, `index.html`,
`wwwroot/index.html`, and `public/index.html` are tried in that order. That path is resolved
against the project, not the current directory, so serving a project from elsewhere works.

Live reload watches the project and rebuilds incrementally on every save. A small script,
`/__csjs/client.js`, is injected into every HTML page served, and listens on a Server-Sent Events
stream at `/__csjs/events`. What happens on a change:

| Change | In the page |
| --- | --- |
| A `.css` file only | The matching `<link rel="stylesheet">` is swapped in place — no reload, no lost state |
| C#, the host page, or any other asset | The page reloads |
| A build error | The errors are drawn over the page, which keeps showing the last good build |

This is live reload for code and hot swapping for CSS, not hot module replacement: JavaScript
modules cannot be replaced in a running page without the code's cooperation, so a code change
costs a reload. Responses are sent with `Cache-Control: no-store`, so a reload always fetches what
the rebuild wrote. If the very first build fails, `serve` starts anyway and shows the errors;
with `--no-reload` it exits non-zero as it always did.

## `cs2js lsp`

Runs the CSJS language server over stdin/stdout, for editors. It is what the VS Code extension
starts, and any LSP client can use it: completion, hover, signature help, go to definition, and
diagnostics — the C# compiler's and the transpiler's, so `CSJS0002` is underlined as it is typed.

```bash
cs2js lsp
dotnet cs2js lsp
```

The project is the nearest `csjs.json` above an opened file, loaded exactly as `build` loads it.
Not meant to be run by hand; see [Language server](language-server.md) for editor setup.

## `cs2js bind <file.d.ts>`

Generates C# bindings from TypeScript declarations. See [bindings.md](bindings.md) for what each
declaration becomes.

```bash
cs2js bind types/chart.d.ts --module chart.js --out src/Bindings/Chart.cs
cs2js bind node_modules/canvas-confetti/index.d.ts --module canvas-confetti --global
```

| Option | Meaning |
| --- | --- |
| `--module <specifier>` | The module the generated `[JsImport]`s name. Defaults to the specifier of a `declare module "x"` block, or `./<file>.js`. A script with no imports or exports binds through `[JsGlobal]` instead. |
| `--namespace <ns>` | The C# namespace. Defaults to `Bindings.<Name>`. |
| `--out <file.cs>` | Write here. Without it the code goes to stdout and the summary to stderr. |
| `--global` | Bind through globals — a UMD library loaded with a script tag, reached through its `export as namespace` name. |

Imports and re-exports are followed: relative ones beside the file, bare ones through
`node_modules`, walking up, with `@types/<name>` as a fallback.

## `cs2js add <package>[@version] [directory]`

Fetches a package from the npm registry, finds its typings, generates bindings, and writes them
to `<source>/Bindings/<Name>.cs`.

```bash
cs2js add three
cs2js add vue@3.5
cs2js add @angular/core ./client --namespace Client.Angular
```

- Metadata comes from `https://registry.npmjs.org`; the tarball is unpacked in memory with
  `GZipStream` and `TarReader`, so neither Node nor npm is needed.
- Typings are found through `exports` conditions, `types` / `typings`, `index.d.ts`, or `main`,
  and failing all of those, the `@types/<name>` package for the same major version.
- `export * from "dep"` is followed into the dependency at the range the package declares —
  Vue's entry point is seven lines that re-export four other packages.
- The version can be exact, a dist-tag, or a range (`^`, `~`, x-ranges, comparisons, `||`).

The package is recorded in `csjs.json` under `dependencies`, pinned to the exact version the
bindings came from, and an import map for serving the page as-is is printed. Why the record lives
there is in [bindings.md](bindings.md#cs2js-add-where-the-dependency-is-recorded).

## `cs2js version`

---

## `csjs.json`

Every field has a default, so the file is optional.

| Field | Default | Meaning |
| --- | --- | --- |
| `name` | directory name | Project name; also the assembly name Roslyn uses. |
| `source` | `"src"` | Where sources live. Falls back to the project directory. |
| `output` | `"dist"` | Where output goes. |
| `include` | `["*.csjs", "*.cs"]` | Which files to transpile. |
| `exclude` | `["bin", "obj", "node_modules", "dist"]` | Directory names to skip. |
| `module` | `"esm"` | `esm` or `script`. `script` omits import/export entirely. |
| `typeDefinitions` | `true` | Emit `.d.ts`. |
| `sourceMaps` | `true` | Emit `.js.map`. Required for the debugging bridge. |
| `strictWarnings` | `false` | Treat C# warnings as errors. |
| `define` | `[]` | Preprocessor symbols. `CSJS` is always defined. |
| `references` | `[]` | Extra assembly paths to bind against. |
| `html` | — | Page served by `cs2js serve`. |
| `assets` | `true` | Copy non-source files into the output, keeping their structure. |
| `bundle` | — | `{ enabled, file, minify, modules }`; see [Bundling](#bundling). |
| `dependencies` | — | npm packages added with `cs2js add`, each pinned to the version its bindings came from. |

---

## Diagnostics

Messages use the C# compiler's format, so editors and CI parse them without configuration:

```
Program.csjs(12,9): error CSJS0002: 'System.Globalization.CultureInfo.Name' has no JavaScript mapping…
```

| ID | Meaning |
| --- | --- |
| `CSJS0001` | Unsupported syntax. |
| `CSJS0002` | An unsupported member, or `ref`/`out` on your own method. |
| `CSJS0004` | An unsupported pattern; also `is IInterface`, which is erased. |
| `CSJS0005` | Pointers or `unsafe`. |
| `CSJS0006` | `lock` has no effect. Warning; the body still runs. |
| `CS####` | From the C# compiler itself. |

---

## MSBuild integration — `CSJS.Build`

The `CSJS.Build` package makes an ordinary `.csproj` transpile on every `dotnet build`, with no
global tool installed:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <CsjsOutputPath>wwwroot/js/</CsjsOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CSJS.Build" Version="0.3.0" />
  </ItemGroup>
</Project>
```

Because the project is real C#, **Visual Studio, Rider, and VS Code's C# Dev Kit give full
IntelliSense** with no CSJS extension, and CSJS diagnostics reach their error lists: the
transpiler prints them in MSBuild's `file(line,col): error CSJS0002: …` form with absolute paths.
`dotnet run` serves `CsjsWebRoot` on `CsjsHostPort`.

| Property | Default | Meaning |
| --- | --- | --- |
| `CsjsOutputPath` | `wwwroot/js/` | Where `.js`, `.d.ts`, and `.js.map` files go |
| `CsjsSourceRoot` | `.` | Folder whose structure the output mirrors (`src` turns `src/App.cs` into `App.js`) |
| `CsjsWebRoot` | `wwwroot/` | What `dotnet run` serves |
| `CsjsHostPort` | `5080` | Port `dotnet run` listens on |
| `CsjsTypeDefinitions` | `true` | Emit `.d.ts` |
| `CsjsSourceMaps` | `true` | Emit source maps |
| `CsjsModule` | `esm` | `esm` or `script` |
| `CsjsEntryPoint` | `false` | Append a call to `Main()` |
| `CheckForOverflowUnderflow` | `false` | `checked` arithmetic by default — the C# compiler's own switch |
| `CsjsEnabled` | `true` | `false` compiles without transpiling |

How it works: after `CoreCompile`, the targets write the project's `Compile` items to a response
file and run the CLI the package carries under `tools/` — `cs2js compile`. Files generated under
`obj/` (`GlobalUsings.g.cs`, `AssemblyInfo.cs`) are compiled for their declarations but not
emitted. The CLI runs out of process on purpose: Visual Studio hosts MSBuild on .NET Framework
with its own Roslyn loaded, and CSJS needs .NET 10 and Roslyn 5.

The `csjs-app`, `csjs-lib`, `csjs-aspnet`, and `csjs-blazor` templates in `CSJS.Templates` all use
it. In the two server templates the browser code is its own project under `CsjsClient/`, which
the web project references with `ReferenceOutputAssembly="false"` purely so it builds first.

## `cs2js compile` and `cs2js host`

The two commands `CSJS.Build` drives. You rarely need them directly.

- `cs2js compile --project-dir D --out O --sources S.rsp [--support X.rsp] [--source-root R]
  [--define A;B] [--no-dts] [--no-maps] [--check-overflow] [--entry] [--module script]` transpiles
  an explicit list of files, writing only outputs whose content changed.
- `cs2js host [dir] [--port N]` serves a folder over HTTP without building anything — ES modules
  do not load from `file://`.

---

Gravicode Studios — led by Kang Fadhil.
