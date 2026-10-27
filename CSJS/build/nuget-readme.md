# CSJS

**C# that runs as JavaScript.** A transpiler, an interop layer, a CLI, an editor with an AI
assistant, and a browser playground — so .NET developers can write for the web without leaving C#.

You write C#:

```csharp
using CSJS;
using CSJS.Browser;

[JsExport(Global = true)]
public static class App
{
    public static void Start()
    {
        var heading = Document.QuerySelector("h1");
        if (heading is not null) heading.TextContent = Greet("World");
    }

    public static string Greet(string name) => $"Hello, {name}!";
}
```

You get JavaScript — readable, module-shaped, tree-shakeable — plus a `.d.ts` and a source map:

```javascript
export class App {
  static Start() {
    const heading = document.querySelector('h1');
    if (!(heading === null)) {
      heading.textContent = App.Greet('World');
    }
  }

  static Greet(name) {
    return `Hello, ${name}!`;
  }
}
globalThis.App = App;
```

## The packages

| | |
| --- | --- |
| `CSJS.Core` | The engine: Roslyn front end, JavaScript emitter, `.d.ts` emitter, source maps. |
| `CSJS.Interop` | Interop attributes and typed browser bindings. Reference this from your client code. |
| `CSJS.Web` | The whole web platform as typed C# — every `lib.dom` interface, 1,464 types, with MDN docs. |
| `CSJS.Cli` | `dotnet cs2js build` — build, run, watch, serve, new. |
| `CSJS.Cli.Standalone` | The same tool under the shorter `cs2js` command. |
| `CSJS.Testing` | Write tests in C#, run them on a JavaScript runtime. |
| `CSJS.Build` | MSBuild integration: `dotnet build` transpiles an ordinary `.csproj`, CSJS errors land in the IDE error list, `dotnet run` serves the page. |
| `CSJS.Templates` | `dotnet new` templates: browser app, npm library, ASP.NET + C# client, Blazor + CSJS, console-to-web. |

## Getting started

With the templates — no global tool, full IntelliSense in Visual Studio, Rider, and VS Code:

```bash
dotnet new install CSJS.Templates
dotnet new csjs-app -n HelloWorld
cd HelloWorld
dotnet run
```

Or with the CLI:

```bash
dotnet tool install --global CSJS.Cli.Standalone   # gives  cs2js build
cs2js new HelloWorld
cd HelloWorld
cs2js serve                                        # live reload on save
```

Then open <http://localhost:5080>. `cs2js lsp` is a language server for any LSP editor, and
`cs2js add <npm-package>` generates C# bindings from a package's TypeScript declarations.

## How types map

This table is the contract — the emitter, the `.d.ts` writer, and the IntelliSense provider all
read it, so they cannot disagree.

| C# | JavaScript | TypeScript |
| --- | --- | --- |
| `int`, `double`, `decimal` | `number` | `number` |
| `long`, `ulong` | `BigInt` | `bigint` |
| `string`, `char` | `string` | `string` |
| `T[]`, `List<T>` | `Array` | `T[]` |
| `Dictionary<K,V>` | `Map` | `Map<K, V>` |
| `HashSet<T>` | `Set` | `Set<T>` |
| `Task`, `Task<T>` | `Promise` | `Promise<T>` |
| `IEnumerable<T>` from an iterator | generator | `Generator<T>` |
| `enum` | frozen object, both directions | const object + union type |
| `record` | class with value equality | class |
| `DateTime` | `Date` | `Date` |
| `TimeSpan` | milliseconds | `number` |
| `struct` | class with `$clone()` on copy | class |
| `ref` / `out` parameters | extra elements in the result | tuple result |

There is no runtime shim layer: a `List<T>` is a real `Array`, a `Dictionary<K,V>` a real `Map`.

**Unmapped framework members are build errors, not guesses.** Emitting a call verbatim and
letting the browser discover it does not exist is the worse failure mode.

Covered through C# 14 — `field`, extension members, null-conditional assignment — along with
full LINQ query syntax, `checked` arithmetic, struct value semantics, `Regex`, `System.Text.Json`,
and `TimeSpan`. Whole programs are checked against .NET's own output.

**Not supported, and reported rather than mistranslated:** pointers and `unsafe`, and unmapped
BCL members.

## Documentation

Full documentation, samples, templates, and the desktop editor live in the repository:

<https://github.com/DotNetVibeCoderz/Vibe_Language/tree/main/CSJS>

## License

MIT.

---

Built by **Gravicode Studios**, led by **Kang Fadhil**.
