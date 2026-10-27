# Project templates

Five starting points, shipped as the `CSJS.Templates` NuGet package:

```bash
dotnet new install CSJS.Templates
```

| Short name | Template | For |
| --- | --- | --- |
| `csjs-app` | [Browser App](csjs-app) | A web page written in C#. `dotnet run` builds and serves it. |
| `csjs-lib` | [JavaScript Library](csjs-lib) | A library written in C#, published to npm with `.d.ts` typings. |
| `csjs-aspnet` | [ASP.NET + JS](csjs-aspnet) | An ASP.NET Core app whose browser code is C#. |
| `csjs-blazor` | [Blazor Hybrid](csjs-blazor-hybrid) | Blazor Server for structure, CSJS for client-side rendering. |
| `csjs-console` | [Console to Web](csjs-console-web) | A C# console program that runs in the browser. |

```bash
dotnet new csjs-app -n MyApp && cd MyApp && dotnet run
dotnet new csjs-lib -n my-lib && cd my-lib && npm test
```

## No global tool needed

Every template except `csjs-console` is an ordinary `.csproj` that references
[`CSJS.Build`](../src/CSJS.Build). `dotnet build` compiles the C#, then transpiles it; CSJS errors
appear in the IDE error list like compiler errors, and Visual Studio, Rider, and VS Code give full
IntelliSense because the code is real C#. See [docs/cli.md](../docs/cli.md#msbuild-integration--csjsbuild)
for the properties.

In the two server templates the browser code is its own project under `CsjsClient/`. The web
project excludes it from its own compilation and references it only so it builds first:

```xml
<ItemGroup>
  <Compile Remove="CsjsClient/**" />
  <Content Remove="CsjsClient/**" />
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="CsjsClient/MyApp.Client.csproj" ReferenceOutputAssembly="false" />
</ItemGroup>
```

`csjs-console` uses `csjs.json` and the CLI instead (`cs2js serve`), which is the lighter shape for
a single page with no server.

## Versions

Each template references `CSJS.Build` at the version written in the repository; packing
`CSJS.Templates` rewrites it to the template package's own version, so a template always pulls the
transpiler it was released with. CI installs the packed templates, builds a project from each, and
runs the library template's tests on Node.

## Where assets go

Two locations, and they behave differently. Both were verified by adding real files and
fetching them over HTTP from the running app.

| Put it in | What happens | Served at |
| --- | --- | --- |
| `wwwroot/…` | Untouched by the transpiler; ASP.NET serves it | `/img/banner.png` |
| `CsjsClient/…` (built with `cs2js build CsjsClient`) | Copied to the client's output root, structure intact | `/js/assets/icons/cart.svg` |

Stylesheets, images, and fonts belong in `wwwroot/`, exactly as in any other ASP.NET project —
that path never touches CSJS at all. `CsjsClient/` is for assets that belong to the client code
itself, and they land under `/js/` because that is where `CsjsClient/csjs.json` points its
output. Change `"output"` there if you want them somewhere else.

The console template has one location: everything beside `csjs.json` is copied into `dist/`
with its folder structure preserved.

---

Gravicode Studios — led by Kang Fadhil.
