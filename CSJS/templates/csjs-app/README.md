# CsjsApp

A browser app written in C# with [CSJS](https://github.com/DotNetVibeCoderz/Vibe_Language/tree/main/CSJS).

```bash
dotnet run          # builds, transpiles, and serves http://localhost:5080
```

- `App.cs` is the page's behaviour. `dotnet build` transpiles it to `wwwroot/js/App.js`, with a
  `.d.ts` and a source map, so the browser debugger shows your C#.
- `wwwroot/` is the site: `index.html`, the stylesheet, and the generated `js/` folder.
- This is an ordinary `.csproj`, so Visual Studio, Rider, and VS Code give full IntelliSense.
  Anything that has no JavaScript equivalent is reported as a build error (`CSJS0002`) in the
  error list, next to the compiler's own.

Settings you can put in the `.csproj`:

| Property | Default | Meaning |
| --- | --- | --- |
| `CsjsOutputPath` | `wwwroot/js/` | Where the JavaScript goes |
| `CsjsWebRoot` | `wwwroot/` | What `dotnet run` serves |
| `CsjsSourceMaps` | `true` | Emit `.js.map` files |
| `CsjsTypeDefinitions` | `true` | Emit `.d.ts` files |
| `CheckForOverflowUnderflow` | `false` | `checked` integer arithmetic everywhere |
| `CsjsEnabled` | `true` | Set to `false` to skip transpiling |

---

Gravicode Studios — led by Kang Fadhil.
