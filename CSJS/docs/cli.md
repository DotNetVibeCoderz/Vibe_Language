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

## `cs2js run [directory | file]`

Builds, then executes with Node. The entry point is the first of `Program.js`, `Main.js`,
`main.js`, or `index.js` found in the output.

```bash
cs2js run                # the project
cs2js run Scratch.csjs   # a single file, transpiled to a temporary directory
```

Requires a class with a static `Main`.

## `cs2js watch [directory]`

Rebuilds whenever a `.cs`, `.csjs`, or `.json` file changes. Writes are debounced by 150 ms,
because editors save in several steps and rebuilding against a half-written file is noise.

## `cs2js serve [directory]`

Builds, then serves the output over HTTP.

```bash
cs2js serve --port 8080
```

The `html` entry in `csjs.json` is copied in as `index.html`. Without it, `index.html`,
`wwwroot/index.html`, and `public/index.html` are tried in that order. That path is resolved
against the project, not the current directory, so serving a project from elsewhere works.

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

## MSBuild integration

Transpile before a server build so the browser code is always current:

```xml
<Target Name="TranspileCsjs" BeforeTargets="Build">
  <Exec Command="dotnet cs2js build &quot;$(MSBuildProjectDirectory)/CsjsClient&quot;" />
</Target>
```

Client sources must be excluded from the C# compilation — they are transpiled, not compiled:

```xml
<ItemGroup>
  <Compile Remove="CsjsClient/**" />
  <Content Remove="CsjsClient/**" />
  <None Include="CsjsClient/**" />
</ItemGroup>
```

Both templates under [`templates/`](../templates) do exactly this.

---

Gravicode Studios — led by Kang Fadhil.
