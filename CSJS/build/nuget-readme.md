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
| `CSJS.Cli` | `dotnet cs2js build` — build, run, watch, serve, new. |
| `CSJS.Cli.Standalone` | The same tool under the shorter `cs2js` command. |
| `CSJS.Testing` | Write tests in C#, run them on a JavaScript runtime. |

## Getting started

```bash
dotnet tool install --global CSJS.Cli.Standalone   # gives  cs2js build
dotnet tool install --global CSJS.Cli              # gives  dotnet cs2js build

cs2js new HelloWorld
cd HelloWorld
cs2js serve
```

Then open <http://localhost:5080>.

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

There is no runtime shim layer: a `List<T>` is a real `Array`, a `Dictionary<K,V>` a real `Map`.

**Unmapped framework members are build errors, not guesses.** Emitting a call verbatim and
letting the browser discover it does not exist is the worse failure mode.

**Not supported, and reported rather than mistranslated:** `ref` and `out` on your own methods
(return a tuple instead), pointers and `unsafe`, and unmapped BCL members.

## Documentation

Full documentation, samples, templates, and the desktop editor live in the repository:

<https://github.com/DotNetVibeCoderz/Vibe_Language/tree/main/CSJS>

## License

MIT.

---

Built by **Gravicode Studios**, led by **Kang Fadhil**.
