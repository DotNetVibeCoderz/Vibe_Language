# Releasing

Five packages ship from this repository:

| Package | What it is |
| --- | --- |
| `CSJS.Core` | The transpiler engine. |
| `CSJS.Interop` | Interop attributes and browser bindings. |
| `CSJS.Cli` | The tool under `dotnet cs2js`. |
| `CSJS.Cli.Standalone` | The same tool under `cs2js`. |
| `CSJS.Testing` | C# tests executed on a JavaScript runtime. |

Shared metadata — version, authors, URLs, licence, README — lives in `Directory.Build.props`,
so it is set once rather than five times.

## Where the workflow lives

Not in this directory. GitHub reads workflows only from `.github/workflows/` at a **repository
root**, and CSJS is one of several projects inside `Vibe_Language`:

```
Vibe_Language/
  .github/workflows/csjs-ci.yml   ← here, and this is the only place it runs
  CSJS/
  BosoEngine/
  GoSharp/
```

The name is prefixed so the sibling projects can add their own workflows without colliding.

A `Locate CSJS` step checks the repository root and then `CSJS/`, so the same file still works
if CSJS is ever split into a repository of its own.

**Pull requests are filtered to `CSJS/**`; pushes are not.** A `paths` filter on `push` would
also apply to tag pushes, and a release tag GitHub decided had not touched `CSJS/` would skip
publishing without saying anything. Pull requests carry the filter, which is where the volume
is; a push to `main` runs the whole thing regardless of which project changed.

## What CI does

| Job | Runs on | Why |
| --- | --- | --- |
| `test` | Ubuntu | Builds the engine and runs the test suite. Node is installed because the semantics tests execute the emitted JavaScript. |
| `windows` | Windows | Builds the whole solution. CSJSGen targets `net10.0-windows`, so it cannot be checked anywhere else. |
| `pack` | Ubuntu | Packs all five packages and uploads them as an artifact. |
| `smoke` | Ubuntu | Installs the *packed* tool, scaffolds a project, builds it, and runs it. Unit tests cover the engine; this covers the packaging. |
| `publish` | Ubuntu | Pushes to nuget.org. Tags only. |

Every push produces downloadable packages. Only a tag publishes.

## Cutting a release

1. Update `VersionPrefix` in `Directory.Build.props`.
2. Commit, then tag with a matching `v` prefix:

   ```bash
   git tag v0.2.0
   git push origin v0.2.0
   ```

3. The `publish` job packs at that version and pushes it.

Builds that are not tags get a `--version-suffix ci.<run>` prerelease suffix, so a package
downloaded from a branch build can never be mistaken for a release.

## The API key

`publish` reads `secrets.NUGET_API_KEY` from an environment named `nuget`. Create it under
**Settings → Environments**, and add a required reviewer if you want publishing to pause for
approval.

Generate the key at <https://www.nuget.org/account/apikeys>. Scope it to **Push** and, for the
first publish, to the glob `CSJS.*` — the package IDs do not exist yet, so an ID-scoped key
cannot be created until after they do.

## Publishing by hand

The same thing CI does, if you would rather do it once yourself:

```bash
dotnet pack src/CSJS.Core        -c Release
dotnet pack src/CSJS.Interop     -c Release
dotnet pack src/CSJS.Testing     -c Release
dotnet pack src/CSJS.Cli         -c Release
dotnet pack src/CSJS.Cli.Standalone -c Release

dotnet nuget push "artifacts/nupkg/*.nupkg" \
  --api-key "$NUGET_API_KEY" \
  --source https://api.nuget.org/v3/index.json \
  --skip-duplicate
```

**This cannot be undone.** A published version can be unlisted but never deleted, and the
package ID is reserved to the account that first pushed it. Check the metadata before pushing:

```bash
unzip -p artifacts/nupkg/CSJS.Core.0.1.0.nupkg '*.nuspec'
```

The `.snupkg` symbol packages are pushed by the same command; nuget.org routes them to the
symbol server on its own.

---

Gravicode Studios — led by Kang Fadhil.
