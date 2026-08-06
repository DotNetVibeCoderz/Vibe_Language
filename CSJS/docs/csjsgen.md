# CSJSGen

The desktop editor for CSJS, with **Jack The Code Bender** beside it.

![CSJSGen](images/csjsgen-main.png)

---

## Running it

```bash
dotnet run --project src/CSJSGen
```

Windows only: the Blazor UI is hosted in a WebView2, which ships with Windows.

---

## The workspace

Three columns, each of which can be hidden from **Tampilan** or the toolbar, and resized by
dragging the divider between them (or with arrow keys once the divider has focus):

- **Project explorer** — a file tree with folders that expand and collapse, file-type badges, and
  a marker on the file you have open.
- **Editor** — Monaco, so C# highlighting, folding, and bracket matching come from a real editor
  rather than an approximation. Tabs across the top; cut/copy/paste, go-to-line (`Ctrl+G`), find
  and replace (`Ctrl+F`, `Ctrl+H`), and formatting (`Ctrl+Alt+F`) are Monaco's own actions.
  Transpiler errors appear in the margin, not only in the pane below.
- **Properties** — facts about the file, and about what compiling it produced: emitted size, the
  source-to-output ratio, whether the runtime was needed, and the types you declared.

Below the editor sits the **output pane** with three tabs: diagnostics, the generated JavaScript,
and the generated `.d.ts`.

Between them runs the **seam** — a gradient from violet (source) to amber (output). Compiling
sends a pulse along it. The colour is not decoration: violet means C# side and amber means
emitted side, consistently, everywhere in the app.

### Keyboard

| | |
| --- | --- |
| `F6` | Compile |
| `F5` | Run on Node |
| `Shift+F5` | Run in the browser |
| `Shift+F6` | Stop whatever is running |
| `Ctrl+S` | Save |
| `Ctrl+Shift+S` | Save all |
| `Ctrl+N` / `Ctrl+Shift+N` | New file / new project |
| `Ctrl+O` / `Ctrl+Shift+O` | Open file / open project |
| `Ctrl+G` | Go to line |
| `Ctrl+F` / `Ctrl+H` | Find / replace |
| `Ctrl+Alt+F` | Format the document |

The build and run keys work wherever focus is, including inside the editor. The rest of Monaco's
own bindings — multi-cursor, fold, move line — are available as usual.

### Running what you build

Three buttons, because a project can be one of two very different things.

| | |
| --- | --- |
| **Compile** | Transpiles and shows the JavaScript and `.d.ts`, with diagnostics in the margin. |
| **Jalankan** | Builds the whole project and runs it on Node, streaming output as it arrives. |
| **Browser** | Builds, serves, and opens the system browser. |
| **Hentikan** | Ends the Node process and the preview server. Appears only while one is running. |

Node has no DOM, so a project that touches `document` cannot be seen by running it there — that
is what **Browser** is for, and the output pane says so rather than showing the raw
`ReferenceError`. Conversely a background service or a chat server has no page at all, and those
run on Node until **Hentikan** stops them.

The run builds the *project*, not the open file. A file that references a type declared in a
sibling — the bindings file of a three.js project, say — cannot be compiled alone.

### Project and file lifecycle

Both projects and individual `.csjs` files support new, open, save, save-as, and close, from the
**Berkas** menu, with the accelerators the menu prints. The project location fields carry a
**Telusuri…** button that opens the system folder picker, so a path is something you pick
rather than something you have to spell.

**Berkas → Proyek baru** opens a gallery of 21 templates across six categories, from a blank
project to a three.js scene, an Electron desktop app, and a WebSocket chat server. Each one is a working application that builds, not a stub — so the
gallery also serves as a way to see what CSJS can do.

![New project gallery](images/csjsgen-new-project.png)

---

## Jack The Code Bender

A chat panel with multiple sessions — create, delete, reset — and Markdown rendering that
handles tables, media, and code blocks. Every code block gets a copy button; the first C# block
in a reply gets **Terapkan ke editor**.

### Attachments

Images become image content parts the model can look at. Documents become a link appended to the
message. The distinction matters: a model can see the first and only fetch the second.

### Providers

OpenAI, Azure OpenAI, Anthropic, Gemini, DeepSeek, and Ollama, selected in **Pengaturan**. Each
provider keeps its own model, key, and endpoint, so switching back does not mean re-entering
credentials. Keys can also come from the environment: `OPENAI_API_KEY`, `AZURE_OPENAI_API_KEY`,
`ANTHROPIC_API_KEY`, `GEMINI_API_KEY`, `DEEPSEEK_API_KEY`.

Ollama runs locally and needs no key.

Reasoning models — the o-series and the gpt-5 family — reject `temperature`, `top_p`, and
`max_tokens` outright, and need `max_completion_tokens` instead. CSJSGen detects them and sends
the right shape, so picking one is not a 400.

### Kernel functions

Jack works through kernel functions, which is what lets him verify his own work rather than
assert that code compiles:

| Function | What it does |
| --- | --- |
| `Csjs.compile` | Transpile and report real diagnostics. |
| `Csjs.run` | Execute on Node and return what it printed. 20-second timeout. |
| `Csjs.emit_type_definitions` | Show the generated `.d.ts`. |
| `Csjs.check_support` | Probe whether a C# feature transpiles. |
| `Csjs.read_editor` | See what is open. |
| `Csjs.write_editor` | Replace the whole file. |
| `Csjs.edit_editor` | Replace an exact snippet, leaving the rest untouched. |
| `Csjs.insert_into_editor` | Add code at the top, bottom, or after an anchor. |
| `Web.search` | Tavily search, when a key is configured. |
| `Web.scrape_page` | Fetch a page as readable text. |
| `Web.read_file_from_url` | Download a text file verbatim. |
| `Utility.current_datetime` | The real date, not a guess. |
| `Utility.calculate`, `statistics` | Arithmetic that has to be right. |
| `Utility.format_number`, `encode`, `count_tokens_estimate` | Odds and ends. |

The three editing functions compile the result and report the verdict, so a broken edit is
caught while Jack can still fix it rather than being discovered by you at the next Compile.

**Adding a capability means adding a kernel function.** The chat loop never changes.

### Prompt templates

An empty session shows a gallery of starter prompts across seven categories — Mulai, Interaksi,
Data, Visual, Permainan, Interop, Belajar. They double as documentation: each one demonstrates a
CSJS idiom rather than just producing output.

---

## Configuration

Everything lives in `app.config`, so persona and model change without a rebuild. That file is
gitignored — copy the template and fill in the provider you want:

```bash
copy src\CSJSGenpp.config.template src\CSJSGenpp.config
```

Keys can also come from the environment, which is the safer habit: `OPENAI_API_KEY`,
`AZURE_OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, `GEMINI_API_KEY`, `DEEPSEEK_API_KEY`.

```xml
<add key="Ai.Provider" value="OpenAI" />
<add key="Ai.OpenAI.Model" value="gpt-4o" />
<add key="Ai.OpenAI.ApiKey" value="" />
<add key="Ai.AzureOpenAI.Deployment" value="gpt-4o" />
<add key="Ai.Temperature" value="0.7" />
<add key="Ai.MaxTokens" value="4096" />
<add key="Ai.SystemPrompt" value="You are Jack The Code Bender…" />
<add key="Tools.Tavily.ApiKey" value="" />
<add key="App.Theme" value="dark" />
```

The settings dialog writes back to this file.

---

## Themes

Dark and light, toggled from the toolbar or **Tampilan**. The choice persists to `app.config`,
and the native window frame follows it so the shell does not fight the UI inside it.

---

Gravicode Studios — led by Kang Fadhil.
