# Language server

Real C# IntelliSense for CSJS — completion, hover, signature help, go to definition — with the
transpiler's own diagnostics inline, from one implementation that every editor shares.

```
                     CSJS.LanguageService
            ┌─────────────────────────────────────┐
            │ Roslyn AdhocWorkspace               │   completion, quick info,
            │   same references + parse options   │── signature help, definition,
            │   as Transpiler                     │   compiler diagnostics
            │ CSJS.Core.Transpiler                │── CSJS0001 … CSJS0006
            │ MemberMap / TypeMap / NameResolver  │── "emits document.querySelector(…)"
            └─────────────────────────────────────┘
                 ▲                         ▲
       in-process│                         │JSON-RPC over stdio
                 │                         │
        CSJSGen (Monaco)              cs2js lsp ── VS Code, Neovim, Rider (LSP4IJ),
                                                  Visual Studio, any LSP client
```

Before this, both editors approximated the language syntactically: a regular expression decided
that `Document.` was being typed and offered a hard-coded member list. Now the answer is the C#
compiler's.

---

## Why the editor agrees with the build

Three things are shared rather than reimplemented, and each closes a way the two could drift:

- **The same compilation.** The workspace references exactly what `Transpiler.BuildReferences`
  binds against — the host framework's assemblies, `CSJS.Interop`, `CSJS.Testing`, and the
  project's `references` — with the same parse options: `LanguageVersion.Preview`, documentation
  comments parsed, and `CSJS` plus the project's `define` symbols. A member the editor offers is
  a member the build accepts.
- **The transpiler itself for diagnostics.** C# errors come from the workspace; CSJS errors come
  from running `CSJS.Core.Transpiler` over the same documents. `CSJS0002` — a framework member
  with no JavaScript mapping — is underlined as it is typed, with the same message the build
  prints. Roslyn diagnostics the build ignores (`CS5001` and friends) are hidden here too, from
  the same list (`Transpiler.IgnoredRoslynDiagnostics`).
- **The mapping tables for hover.** Hovering a mapped BCL member shows the template from
  `MemberMap`; a binding member shows the name `NameResolver` gives it; a framework type shows its
  `TypeMap` translation. The hover cannot describe a translation the emitter does not perform.

`CSJS.Core` diagnostics now carry an end position (`CsjsDiagnostic.EndLine`/`EndColumn`), so a
`CSJS0002` underlines the whole call rather than a guessed width.

---

## Features

| Request | Source |
| --- | --- |
| Completion | Roslyn `CompletionService`; details (signature, docs) resolved lazily per item. |
| Hover | Roslyn `QuickInfoService`, plus the CSJS section described above. |
| Signature help | Semantic model: the enclosing argument list's member group, active overload and argument. |
| Go to definition | `SymbolFinder` — within the project's own sources. |
| Diagnostics | Workspace compiler diagnostics + `Transpiler` diagnostics, pushed after a pause in typing. |

BCL documentation comes from the targeting pack beside the runtime
(`packs/Microsoft.NETCore.App.Ref`) when the machine has an SDK. It changes nothing about binding;
it only gives hover and completion something to say.

---

## `cs2js lsp`

```bash
cs2js lsp          # or: dotnet cs2js lsp
```

Speaks LSP over stdin/stdout with `Content-Length` framing. Nothing else is ever written to
stdout during a session — console output is redirected to stderr, which is where the server logs.

**Projects.** A document belongs to the nearest `csjs.json` above it. That project is loaded the
way `cs2js build` loads it — `CsjsProject` resolves its sources, defines, and references — so
files that are not open still contribute their types, and their errors reach the Problems panel.
The workspace root is loaded at `initialize`. A file with no `csjs.json` above it is served on its
own. Editing `csjs.json` reloads the project.

**Capabilities.**

| Method | Notes |
| --- | --- |
| `initialize` / `initialized` / `shutdown` / `exit` | `exit` returns 0 after `shutdown`, 1 otherwise. |
| `textDocument/didOpen`, `didChange`, `didSave`, `didClose` | Full-document sync. Closing a project source reverts it to the disk copy. |
| `workspace/didChangeWatchedFiles` | Picks up files changed outside the editor. |
| `textDocument/completion`, `completionItem/resolve` | Triggered on `.`, `<`, `[`. |
| `textDocument/hover` | Markdown. |
| `textDocument/signatureHelp` | Triggered on `(` and `,`; label offsets when the client supports them. |
| `textDocument/definition` | Locations in project sources. |
| `textDocument/publishDiagnostics` | Pushed per project, 350 ms after the last edit. |
| `$/cancelRequest` | Cancels a running request. |

Requests run concurrently; document notifications are applied in order on the read loop, so a
request always sees every edit sent before it. Positions are UTF-16, LSP's default.

---

## Visual Studio Code

The extension in [`vscode-extension/`](../vscode-extension) starts `cs2js lsp` with
[`vscode-languageclient`](https://www.npmjs.com/package/vscode-languageclient) for `.csjs` files
everywhere, and for `.cs` files inside a folder holding `csjs.json`. Run `npm install` in the
extension folder before packaging it.

| Setting | Default | What it does |
| --- | --- | --- |
| `csjs.languageServer.enabled` | `true` | Run the language server. |
| `csjs.languageServer.path` | `""` | The command to start, without `lsp`: a path to `cs2js`, or `dotnet cs2js`. Empty means `cs2js` if it is on PATH, otherwise `dotnet cs2js`. |
| `csjs.languageServer.csharpFiles` | `true` | Also serve `.cs` files in CSJS projects. Turn off if the C# extension should own them. |
| `csjs.trace.server` | `off` | Log the JSON-RPC traffic to the CSJS output channel. |

**CSJS: Restart language server** restarts it. Build, run, serve, watch, and **Show generated
JavaScript** are unchanged; while the server runs, a build no longer adds its own copy of the
diagnostics, since they are already live.

---

## Other editors

Any editor with an LSP client can use `cs2js lsp`. Register it for `.csjs` files; add `.cs` only
for folders with a `csjs.json`, or the editor's own C# support and CSJS will both answer.

### Neovim (0.10+)

```lua
vim.filetype.add({ extension = { csjs = 'csjs' } })

vim.api.nvim_create_autocmd('FileType', {
  pattern = { 'csjs', 'cs' },
  callback = function(args)
    local root = vim.fs.root(args.buf, { 'csjs.json' })
    -- Plain .cs outside a CSJS project belongs to the regular C# server.
    if not root and vim.bo[args.buf].filetype == 'cs' then return end
    vim.lsp.start({
      name = 'cs2js',
      cmd = { 'cs2js', 'lsp' },        -- or { 'dotnet', 'cs2js', 'lsp' }
      root_dir = root or vim.fs.dirname(vim.api.nvim_buf_get_name(args.buf)),
    })
  end,
})
```

For highlighting, `.csjs` is C#: `vim.treesitter.language.register('c_sharp', 'csjs')`.

### JetBrains Rider (and other IntelliJ IDEs) via LSP4IJ

1. Install the **LSP4IJ** plugin (Red Hat) from the JetBrains Marketplace.
2. **Settings → Languages & Frameworks → Language Servers → +**.
3. **Server:** name `CSJS`, command `cs2js lsp` (or `dotnet cs2js lsp`).
4. **Mappings → File name patterns:** `*.csjs`, language id `csjs`.

Rider's own engine already owns `.cs` files, so map only `*.csjs` there; mapping `*.cs` as well
gives two completion lists. To get C# highlighting for `.csjs`, associate the extension with C#
under **Settings → Editor → File Types** — LSP4IJ keeps serving it either way.

### Visual Studio 2022

Visual Studio has no settings-only LSP registration; a server is attached by a small extension.
With the [LSP client SDK](https://learn.microsoft.com/visualstudio/extensibility/adding-an-lsp-extension)
(`Microsoft.VisualStudio.LanguageServer.Client`) the whole client is one class:

```csharp
[ContentType("csjs")]
[Export(typeof(ILanguageClient))]
public sealed class CsjsLanguageClient : ILanguageClient
{
    public string Name => "CSJS";
    public IEnumerable<string>? ConfigurationSections => null;
    public object? InitializationOptions => null;
    public IEnumerable<string> FilesToWatch => new[] { "**/csjs.json" };
    public bool ShowNotificationOnInitializeFailed => true;

    public event AsyncEventHandler<EventArgs>? StartAsync;
    public event AsyncEventHandler<EventArgs>? StopAsync;

    public Task<Connection?> ActivateAsync(CancellationToken token)
    {
        var process = Process.Start(new ProcessStartInfo("cs2js", "lsp")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        return Task.FromResult<Connection?>(
            new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream));
    }

    public async Task OnLoadedAsync() { if (StartAsync is not null) await StartAsync.InvokeAsync(this, EventArgs.Empty); }
    public Task OnServerInitializedAsync() => Task.CompletedTask;
    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo info)
        => Task.FromResult<InitializationFailureContext?>(null);
}

// Map .csjs to a content type that derives from the LSP base content type.
public static class CsjsContentDefinition
{
    [Export, Name("csjs"), BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    internal static ContentTypeDefinition? Csjs;

    [Export, FileExtension(".csjs"), ContentType("csjs")]
    internal static FileExtensionToContentTypeDefinition? CsjsFiles;
}
```

The newer VisualStudio.Extensibility model offers the same through a `LanguageServerProvider`
whose `CreateServerConnectionAsync` starts `cs2js lsp` and returns its pipes. Keep `.cs` with
Visual Studio's own C# service.

### Others

Helix (`languages.toml`), Sublime Text (the LSP package), Zed, and Emacs (`eglot`) all take a
command line and a file pattern: `cs2js lsp` for `*.csjs`.

---

## CSJSGen

CSJSGen is a .NET application, so it skips the protocol and calls `CSJS.LanguageService`
directly. `CodeIntelligence` mirrors `ProjectService` into the workspace — every C# file of the
open project, as a compile would see them — and `MonacoLanguageBridge` answers Monaco's
completion, hover, and signature-help providers through a `DotNetObjectReference`. Each request
carries Monaco's current text, runs on the thread pool, and cancels its own predecessor; live
diagnostics become margin markers once typing pauses for 450 ms. See [CSJSGen](csjsgen.md).

---

## Testing

- `tests/CSJS.Core.Tests/LanguageServiceTests.cs` drives the service the way an editor does:
  completion on a BCL type and on `Document.`, lazy resolution, hover with the emitted
  JavaScript, `CSJS0002` inline with its range, compiler errors, definition across files,
  signature help, the `CSJS` define.
- `tests/CSJS.Core.Tests/LspServerTests.cs` launches the real `cs2js lsp` process and runs a
  scripted session over stdio — `initialize`, `didOpen`, pushed `CSJS0002`, hover, definition into
  an unopened file, `didChange`, completion and resolve, signature help, `shutdown`/`exit` — with
  URIs in VS Code's percent-encoded spelling.

---

## Limitations

- **CSJS diagnostics need a compiling project.** The transpiler stops at the first C# error, as
  a build does, so `CSJS####` diagnostics appear once the C# errors are fixed.
- **Definition stops at the project boundary.** Framework and `CSJS.Interop` symbols have no
  source here, so there is no metadata-as-source view.
- **No rename, references, formatting, code actions, or semantic tokens** yet. Roslyn has all of
  them; each is a handler away.
- **Full document sync.** Fine for the file sizes CSJS projects have; incremental sync would cut
  traffic for very large files.
- **Every keystroke burst costs one transpile** of the whole project for diagnostics. Debouncing
  keeps that to one per pause; incremental compilation is on the roadmap.

---

Gravicode Studios — led by Kang Fadhil.
