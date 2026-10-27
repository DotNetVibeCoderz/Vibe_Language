// CSJS extension for Visual Studio Code.
//
// Everything the extension knows about CSJS semantics comes from the transpiler itself, invoked
// through the cs2js CLI. There is deliberately no second type engine here: a separate analyser
// would drift from the compiler and start reporting things the build does not.
//
// IntelliSense — completion, hover, signature help, go to definition, live diagnostics — comes
// from `cs2js lsp`, the same language service CSJSGen runs in-process. It binds the project
// exactly as `cs2js build` does and runs the transpiler for diagnostics, so CSJS0002 and friends
// appear as you type rather than only after a build.
//
// Gravicode Studios — led by Kang Fadhil.

const vscode = require('vscode');
const cp = require('child_process');
const path = require('path');
const fs = require('fs');

/** @type {vscode.OutputChannel} */
let output;
/** @type {vscode.DiagnosticCollection} */
let diagnostics;
/** @type {vscode.StatusBarItem} */
let status;
/** The running language client, or undefined when the server is off or failed to start. */
let client;

function activate(context) {
  output = vscode.window.createOutputChannel('CSJS');
  diagnostics = vscode.languages.createDiagnosticCollection('csjs');

  status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
  status.command = 'csjs.build';
  status.text = '$(tools) CSJS';
  status.tooltip = 'Transpile the CSJS project';
  status.show();

  context.subscriptions.push(
    output,
    diagnostics,
    status,

    vscode.commands.registerCommand('csjs.build', () => build()),
    vscode.commands.registerCommand('csjs.run', () => runCommand('run')),
    vscode.commands.registerCommand('csjs.serve', () => runInTerminal('serve')),
    vscode.commands.registerCommand('csjs.watch', () => runInTerminal('watch')),
    vscode.commands.registerCommand('csjs.showOutput', () => showGeneratedJavaScript()),
    vscode.commands.registerCommand('csjs.newProject', () => newProject()),
    vscode.commands.registerCommand('csjs.restartLanguageServer', () => restartLanguageServer()),

    vscode.tasks.registerTaskProvider('csjs', taskProvider()),
    vscode.workspace.onDidSaveTextDocument(onSave),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration('csjs.languageServer')) restartLanguageServer();
    }),
    { dispose: () => stopLanguageServer() },
  );

  // IntelliSense is driven by the transpiler's own view of the language, through cs2js lsp.
  return startLanguageServer();
}

// ---------------------------------------------------------------------------------------------
// Building
// ---------------------------------------------------------------------------------------------

function cliCommand() {
  return vscode.workspace.getConfiguration('csjs').get('cliPath', 'cs2js');
}

function projectDirectory() {
  const folder = vscode.workspace.workspaceFolders?.[0];
  if (!folder) return null;

  // A csjs.json anywhere in the tree marks the project root; the workspace root is the fallback.
  const root = folder.uri.fsPath;
  if (fs.existsSync(path.join(root, 'csjs.json'))) return root;

  for (const candidate of ['src', 'client', 'CsjsClient']) {
    if (fs.existsSync(path.join(root, candidate, 'csjs.json'))) return path.join(root, candidate);
  }

  return root;
}

async function build() {
  const directory = projectDirectory();
  if (!directory) {
    vscode.window.showErrorMessage('CSJS: open a folder before building.');
    return;
  }

  status.text = '$(sync~spin) CSJS';
  output.appendLine(`> ${cliCommand()} build "${directory}"`);

  const result = await execute(`${cliCommand()} build "${directory}"`, directory);

  output.appendLine(result.stdout);
  if (result.stderr) output.appendLine(result.stderr);

  const problems = parseDiagnostics(result.stdout + '\n' + result.stderr, directory);

  // With the language server running, the same diagnostics are already live in the Problems
  // panel; adding the build's copy would list every one of them twice.
  if (client) diagnostics.clear();
  else applyDiagnostics(problems);

  const errors = problems.filter((p) => p.severity === vscode.DiagnosticSeverity.Error).length;

  if (errors > 0) {
    status.text = `$(error) CSJS ${errors}`;
    if (vscode.workspace.getConfiguration('csjs').get('showOutputOnBuild', true)) output.show(true);
  } else {
    status.text = '$(check) CSJS';
    setTimeout(() => { status.text = '$(tools) CSJS'; }, 3000);
  }

  return errors === 0;
}

async function runCommand(verb) {
  const editor = vscode.window.activeTextEditor;
  const target = editor?.document.languageId === 'csjs'
    ? `"${editor.document.uri.fsPath}"`
    : `"${projectDirectory()}"`;

  output.show(true);
  output.appendLine(`> ${cliCommand()} ${verb} ${target}`);

  const result = await execute(`${cliCommand()} ${verb} ${target}`, projectDirectory() ?? undefined);
  output.appendLine(result.stdout);
  if (result.stderr) output.appendLine(result.stderr);
}

/** Long-running commands belong in a terminal, where they can be watched and stopped. */
function runInTerminal(verb) {
  const directory = projectDirectory();
  if (!directory) {
    vscode.window.showErrorMessage('CSJS: open a folder first.');
    return;
  }

  const terminal = vscode.window.createTerminal({ name: `CSJS ${verb}`, cwd: directory });
  terminal.sendText(`${cliCommand()} ${verb} "${directory}"`);
  terminal.show();
}

function execute(command, cwd) {
  return new Promise((resolve) => {
    cp.exec(command, { cwd, maxBuffer: 10 * 1024 * 1024 }, (error, stdout, stderr) => {
      resolve({ stdout: stdout ?? '', stderr: stderr ?? '', failed: Boolean(error) });
    });
  });
}

// ---------------------------------------------------------------------------------------------
// Diagnostics
// ---------------------------------------------------------------------------------------------

/**
 * Reads cs2js output into VS Code diagnostics.
 * The CLI prints `File.csjs(line,col): severity ID: message`, matching the C# compiler's format.
 */
function parseDiagnostics(text, projectDirectory) {
  const pattern = /^\s*(.+?)\((\d+),(\d+)\):\s+(error|warning)\s+(\S+):\s+(.*)$/;
  const problems = [];

  for (const line of text.split('\n')) {
    const match = pattern.exec(line.trimEnd());
    if (!match) continue;

    const [, file, lineNumber, column, severity, code, message] = match;

    const range = new vscode.Range(
      Number(lineNumber) - 1, Number(column) - 1,
      Number(lineNumber) - 1, Number(column) + 40,
    );

    const diagnostic = new vscode.Diagnostic(
      range,
      message,
      severity === 'error' ? vscode.DiagnosticSeverity.Error : vscode.DiagnosticSeverity.Warning,
    );

    diagnostic.code = code;
    diagnostic.source = 'csjs';
    diagnostic.file = resolveFile(file, projectDirectory);

    problems.push(diagnostic);
  }

  return problems;
}

function resolveFile(file, projectDirectory) {
  if (path.isAbsolute(file)) return file;

  // Diagnostics are relative to the project's source root, which is usually src/.
  for (const base of [path.join(projectDirectory, 'src'), projectDirectory]) {
    const candidate = path.join(base, file);
    if (fs.existsSync(candidate)) return candidate;
  }

  return path.join(projectDirectory, file);
}

function applyDiagnostics(problems) {
  diagnostics.clear();

  const byFile = new Map();
  for (const problem of problems) {
    const list = byFile.get(problem.file) ?? [];
    list.push(problem);
    byFile.set(problem.file, list);
  }

  for (const [file, list] of byFile) {
    diagnostics.set(vscode.Uri.file(file), list);
  }
}

function onSave(document) {
  if (document.languageId !== 'csjs') return;
  if (!vscode.workspace.getConfiguration('csjs').get('buildOnSave', false)) return;
  build();
}

// ---------------------------------------------------------------------------------------------
// Generated output
// ---------------------------------------------------------------------------------------------

/**
 * Opens the JavaScript a .csjs file transpiles to, beside the source.
 * Seeing the two together is the fastest way to understand what the transpiler did — and it is
 * also what makes a source-mapped breakpoint make sense.
 */
async function showGeneratedJavaScript() {
  const editor = vscode.window.activeTextEditor;
  if (!editor || editor.document.languageId !== 'csjs') {
    vscode.window.showInformationMessage('CSJS: open a .csjs file first.');
    return;
  }

  if (editor.document.isDirty) await editor.document.save();
  const built = await build();
  if (!built) return;

  const directory = projectDirectory();
  const config = readConfig(directory);
  const sourceRoot = path.join(directory, config.source ?? 'src');
  const outputRoot = path.join(directory, config.output ?? 'dist');

  const relative = path.relative(sourceRoot, editor.document.uri.fsPath);
  const generated = path.join(outputRoot, relative.replace(/\.(csjs|cs)$/i, '.js'));

  if (!fs.existsSync(generated)) {
    vscode.window.showWarningMessage(`CSJS: no generated file at ${generated}`);
    return;
  }

  const document = await vscode.workspace.openTextDocument(vscode.Uri.file(generated));
  await vscode.window.showTextDocument(document, {
    viewColumn: vscode.ViewColumn.Beside,
    preview: true,
  });
}

function readConfig(directory) {
  try {
    const configPath = path.join(directory, 'csjs.json');
    if (!fs.existsSync(configPath)) return {};
    return JSON.parse(fs.readFileSync(configPath, 'utf8'));
  } catch {
    return {};
  }
}

async function newProject() {
  const name = await vscode.window.showInputBox({
    prompt: 'Project name',
    value: 'MyCsjsApp',
    validateInput: (value) => (value.trim().length === 0 ? 'A name is required.' : null),
  });

  if (!name) return;

  const folder = vscode.workspace.workspaceFolders?.[0];
  const cwd = folder?.uri.fsPath ?? process.cwd();

  const terminal = vscode.window.createTerminal({ name: 'CSJS', cwd });
  terminal.sendText(`${cliCommand()} new ${name}`);
  terminal.show();
}

// ---------------------------------------------------------------------------------------------
// Tasks
// ---------------------------------------------------------------------------------------------

function taskProvider() {
  return {
    provideTasks() {
      const directory = projectDirectory();
      if (!directory) return [];

      return ['build', 'run', 'watch', 'serve'].map((command) => {
        const task = new vscode.Task(
          { type: 'csjs', command },
          vscode.TaskScope.Workspace,
          command,
          'csjs',
          new vscode.ShellExecution(`${cliCommand()} ${command} "${directory}"`),
          '$csjs',
        );

        task.group = command === 'build' ? vscode.TaskGroup.Build : undefined;
        return task;
      });
    },

    resolveTask(task) {
      const command = task.definition.command;
      if (!command) return undefined;

      const directory = task.definition.project ?? projectDirectory();
      return new vscode.Task(
        task.definition,
        vscode.TaskScope.Workspace,
        command,
        'csjs',
        new vscode.ShellExecution(`${cliCommand()} ${command} "${directory}"`),
        '$csjs',
      );
    },
  };
}

// ---------------------------------------------------------------------------------------------
// IntelliSense: the cs2js language server
// ---------------------------------------------------------------------------------------------

/**
 * Starts `cs2js lsp` as a language client.
 *
 * `.csjs` files are served everywhere. Plain `.cs` files are served only inside a folder that
 * holds a csjs.json, so a repository mixing a .NET backend with a CSJS client keeps its backend
 * C# with the C# extension rather than binding it against browser references.
 */
async function startLanguageServer() {
  const config = vscode.workspace.getConfiguration('csjs');
  if (!config.get('languageServer.enabled', true)) return;

  let languageClient;
  try {
    languageClient = require('vscode-languageclient/node');
  } catch {
    output.appendLine('CSJS: vscode-languageclient is missing — run `npm install` in the extension folder. IntelliSense is unavailable.');
    return;
  }

  const server = resolveServerCommand();
  const serverOptions = {
    command: server.command,
    args: [...server.args, 'lsp'],
    transport: languageClient.TransportKind.stdio,
    options: { cwd: projectDirectory() ?? undefined },
  };

  const documentSelector = [{ scheme: 'file', language: 'csjs' }];
  if (config.get('languageServer.csharpFiles', true)) {
    for (const directory of await csjsProjectDirectories()) {
      documentSelector.push({
        scheme: 'file',
        language: 'csharp',
        pattern: `${directory.replace(/\\/g, '/')}/**/*.cs`,
      });
    }
  }

  const clientOptions = {
    documentSelector,
    outputChannel: output,
    synchronize: {
      // Files changed outside the editor — a checkout, a generator — and csjs.json edits, which
      // change what the project compiles.
      fileEvents: vscode.workspace.createFileSystemWatcher('**/{*.cs,*.csjs,csjs.json}'),
    },
  };

  client = new languageClient.LanguageClient('csjs', 'CSJS Language Server', serverOptions, clientOptions);
  output.appendLine(`> ${server.command} ${[...server.args, 'lsp'].join(' ')}`);

  try {
    await client.start();
    // Build output would now only duplicate what the server reports live.
    diagnostics.clear();
  } catch (error) {
    output.appendLine(`CSJS: the language server did not start: ${error?.message ?? error}`);
    vscode.window.showWarningMessage(
      'CSJS: could not start `cs2js lsp`. Install the CLI or set csjs.languageServer.path.');
    client = undefined;
  }
}

async function stopLanguageServer() {
  const running = client;
  client = undefined;
  if (running) {
    try { await running.stop(); } catch { /* already gone */ }
  }
}

async function restartLanguageServer() {
  await stopLanguageServer();
  await startLanguageServer();
}

/**
 * How to launch the server: the configured path when there is one, `cs2js` when it is on PATH,
 * and `dotnet cs2js` — the CSJS.Cli tool — otherwise.
 */
function resolveServerCommand() {
  const configured = vscode.workspace.getConfiguration('csjs').get('languageServer.path', '').trim();
  if (configured) {
    // A path to an executable, or a command line such as `dotnet cs2js`.
    if (fs.existsSync(configured)) return { command: configured, args: [] };
    const [command, ...args] = configured.split(/\s+/);
    return { command, args };
  }

  if (onPath('cs2js')) return { command: 'cs2js', args: [] };
  return { command: 'dotnet', args: ['cs2js'] };
}

function onPath(name) {
  const extensions = process.platform === 'win32'
    ? (process.env.PATHEXT ?? '.EXE;.CMD;.BAT').split(';').map((e) => e.toLowerCase())
    : [''];

  for (const directory of (process.env.PATH ?? '').split(path.delimiter)) {
    if (!directory) continue;
    for (const extension of extensions) {
      if (fs.existsSync(path.join(directory, name + extension))) return true;
    }
  }
  return false;
}

/** Folders holding a csjs.json, which is what makes a folder's .cs files CSJS sources. */
async function csjsProjectDirectories() {
  const files = await vscode.workspace.findFiles('**/csjs.json', '**/{node_modules,bin,obj,dist}/**', 50);
  return files.map((uri) => path.dirname(uri.fsPath));
}

function deactivate() {
  diagnostics?.clear();
  return stopLanguageServer();
}

module.exports = { activate, deactivate };
