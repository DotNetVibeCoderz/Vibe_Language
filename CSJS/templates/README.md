# Project templates

Three starting points, matching the three shapes the specification calls for.

| | For |
| --- | --- |
| [Console to Web](csjs-console-web) | A C# console program that runs in the browser. |
| [ASP.NET + JS](csjs-aspnet) | An ASP.NET Core app whose browser code is C#. |
| [Blazor Hybrid](csjs-blazor-hybrid) | Blazor Server for structure, CSJS for client-side rendering. |

## Using them

The two server templates transpile before build, so `dotnet run` is all you need:

```bash
cd templates/csjs-aspnet
dotnet run
```

That requires the CLI on PATH:

```bash
dotnet tool install --global CSJS.Cli
```

The console template is transpiled directly:

```bash
cs2js serve templates/csjs-console-web
```

## Installing as `dotnet new` templates

```bash
dotnet new install ./templates/csjs-console-web
dotnet new install ./templates/csjs-aspnet
dotnet new install ./templates/csjs-blazor-hybrid

dotnet new csjs-console -n MyApp
dotnet new csjs-aspnet -n MyWebApp
dotnet new csjs-blazor -n MyHybridApp
```

## Where assets go

Two locations, and they behave differently. Both were verified by adding real files and
fetching them over HTTP from the running app.

| Put it in | What happens | Served at |
| --- | --- | --- |
| `wwwroot/…` | Untouched by the transpiler; ASP.NET serves it | `/img/banner.png` |
| `CsjsClient/…` | Copied to the client's output root, structure intact | `/js/assets/icons/cart.svg` |
| `CsjsClient/src/…` | Copied beside the emitted module | `/js/seed.json` |

Stylesheets, images, and fonts belong in `wwwroot/`, exactly as in any other ASP.NET project —
that path never touches CSJS at all. `CsjsClient/` is for assets that belong to the client code
itself, and they land under `/js/` because that is where `CsjsClient/csjs.json` points its
output. Change `"output"` there if you want them somewhere else.

The console template has one location: everything beside `csjs.json` is copied into `dist/`
with its folder structure preserved.

## How the build hook works

Client sources live in their own directory and are excluded from the C# compilation, because
they are transpiled rather than compiled:

```xml
<ItemGroup>
  <Compile Remove="CsjsClient/**" />
  <Content Remove="CsjsClient/**" />
  <None Include="CsjsClient/**" />
</ItemGroup>

<Target Name="TranspileCsjs" BeforeTargets="Build">
  <Exec Command="dotnet cs2js build &quot;$(MSBuildProjectDirectory)/CsjsClient&quot;" />
</Target>
```

---

Gravicode Studios — led by Kang Fadhil.
