# Getting started

## Install

CSJS ships as two tool packages that differ only in command name. Install whichever invocation
you prefer, or both:

```bash
dotnet tool install --global CSJS.Cli.Standalone   # cs2js build
dotnet tool install --global CSJS.Cli              # dotnet cs2js build
```

Check it:

```bash
cs2js version
```

Node.js is needed for `cs2js run` and for the testing framework. It is not needed to build.

## Your first project

```bash
cs2js new HelloWorld
cd HelloWorld
```

You get:

```
HelloWorld/
├── csjs.json          project settings
├── index.html         the host page
└── src/
    └── Program.csjs   your C#
```

Build and serve it:

```bash
cs2js serve
```

Open <http://localhost:5080>.

## What just happened

`src/Program.csjs` is ordinary C#:

```csharp
using CSJS;
using CSJS.Browser;

[JsExport(Global = true)]
public static class App
{
    public static void Start()
    {
        var heading = Document.QuerySelector("h1");
        if (heading is not null)
        {
            heading.TextContent = "Hello from C#!";
        }
    }
}
```

`cs2js build` turned it into `dist/Program.js`, and `index.html` imports it like any other
module:

```html
<script type="module">
  import { App } from './Program.js';
  App.Start();
</script>
```

`[JsExport(Global = true)]` is what makes `App` reachable — both as a named export and on
`globalThis`.

## The edit loop

```bash
cs2js watch      # rebuild on every save
cs2js serve      # build once, then serve over HTTP
cs2js run        # build, then execute on Node
```

`cs2js run` also accepts a single file, which is the fastest way to try something:

```bash
cs2js run Scratch.csjs
```

For that to print anything, the file needs a class with a static `Main`.

## Reading the output

The generated JavaScript is meant to be read. Open `dist/Program.js` next to your source — it is
the fastest way to build an intuition for what transpiles to what. In VS Code, the CSJS
extension does this for you with **CSJS: Show generated JavaScript**.

You will also find `Program.d.ts` (types for TypeScript consumers) and `Program.js.map` (so
breakpoints in the C# work in the browser).

## Adding a second file

Drop another `.csjs` file in `src/`. Types find each other automatically, and cross-file
references become real imports:

```csharp
// src/Models.csjs
public class User
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}
```

```csharp
// src/Program.csjs
var user = new User { Name = "Ana", Age = 28 };
```

```javascript
// dist/Program.js
import { User } from './Models.js';
```

## Calling a JavaScript library

Declare what you need and the transpiler emits the import:

```csharp
using CSJS;

[JsImport("canvas-confetti")]
public static class Confetti
{
    [JsImport("canvas-confetti", Default = true)]
    public static void Fire() => throw new JsExternException();
}
```

See the [interop guide](interop.md) for the full picture.

## When something will not transpile

CSJS reports it at build time rather than emitting JavaScript that fails later:

```
Program.csjs(12,9): error CSJS0002: 'System.Globalization.CultureInfo.Name' has no JavaScript
mapping and cannot be transpiled. Use a supported member, or declare a binding with [JsGlobal]
or [JsBody].
```

[Language support](language-support.md) lists what works and what to use instead.

## Next steps

- [Language support](language-support.md) — the full picture of what transpiles
- [Interop guide](interop.md) — calling JavaScript from C#
- [CLI reference](cli.md)
- [CSJSGen](csjsgen.md) — the editor with an AI assistant
- Browse [`samples/`](../samples) — four working pages, from hello world to three.js

---

Gravicode Studios — led by Kang Fadhil.
