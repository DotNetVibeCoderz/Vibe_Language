// CSJS extension for Visual Studio Code.
//
// Everything the extension knows about CSJS semantics comes from the transpiler itself, invoked
// through the cs2js CLI. There is deliberately no second type engine here: a separate analyser
// would drift from the compiler and start reporting things the build does not.
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

    vscode.tasks.registerTaskProvider('csjs', taskProvider()),
    vscode.workspace.onDidSaveTextDocument(onSave),

    // IntelliSense is driven by the transpiler's own view of the language.
    vscode.languages.registerCompletionItemProvider('csjs', completionProvider(), '.', '['),
    vscode.languages.registerHoverProvider('csjs', hoverProvider()),
  );
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
  applyDiagnostics(problems);

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
// IntelliSense
// ---------------------------------------------------------------------------------------------

/**
 * Completions for the parts of CSJS that are not plain C#: the interop attributes and the
 * browser bindings. Ordinary C# completion is left to the C# extension, which does it better.
 */
function completionProvider() {
  return {
    provideCompletionItems(document, position) {
      const line = document.lineAt(position).text.slice(0, position.character);

      if (/\[\s*\w*$/.test(line)) return interopAttributeCompletions();
      if (/\bDocument\.$/.test(line)) return memberCompletions(DOCUMENT_MEMBERS);
      if (/\bWindow\.$/.test(line)) return memberCompletions(WINDOW_MEMBERS);

      return undefined;
    },
  };
}

const INTEROP_ATTRIBUTES = [
  ['JsExport', 'JsExport(Global = true)', 'Export the type so host HTML can reach it.'],
  ['JsImport', 'JsImport("${1:module}", "${2:Export}")', 'Bind to a JavaScript module export. Emits a real ES import.'],
  ['JsGlobal', 'JsGlobal("${1:name}")', 'Bind to an existing JavaScript global. No declaration is emitted.'],
  ['JsName', 'JsName("${1:jsName}")', 'Override the emitted JavaScript name.'],
  ['JsBody', 'JsBody("${1:return 0;}")', 'Supply a raw JavaScript body for this method.'],
  ['JsTemplate', 'JsTemplate("${1:{this}[{0}]}")', 'Rewrite every call site into a JavaScript expression.'],
  ['JsObjectLiteral', 'JsObjectLiteral', 'Emit instances as plain object literals rather than a class.'],
  ['JsIgnore', 'JsIgnore', 'Exclude this declaration from transpilation entirely.'],
];

function interopAttributeCompletions() {
  return INTEROP_ATTRIBUTES.map(([label, snippet, detail]) => {
    const item = new vscode.CompletionItem(label, vscode.CompletionItemKind.Property);
    item.insertText = new vscode.SnippetString(snippet);
    item.detail = 'CSJS interop';
    item.documentation = new vscode.MarkdownString(detail);
    return item;
  });
}

const DOCUMENT_MEMBERS = [
  ['QuerySelector', 'querySelector', 'Element?'],
  ['QuerySelectorAll', 'querySelectorAll', 'Element[]'],
  ['GetElementById', 'getElementById', 'Element?'],
  ['CreateElement', 'createElement', 'Element'],
  ['AddEventListener', 'addEventListener', 'void'],
  ['Body', 'body', 'Element'],
  ['Title', 'title', 'string'],
];

const WINDOW_MEMBERS = [
  ['InnerWidth', 'innerWidth', 'double'],
  ['InnerHeight', 'innerHeight', 'double'],
  ['LocalStorage', 'localStorage', 'Storage'],
  ['Location', 'location', 'Location'],
  ['RequestAnimationFrame', 'requestAnimationFrame', 'int'],
  ['AddEventListener', 'addEventListener', 'void'],
  ['Alert', 'alert', 'void'],
];

function memberCompletions(members) {
  return members.map(([name, emitted, type]) => {
    const item = new vscode.CompletionItem(name, vscode.CompletionItemKind.Method);
    item.detail = type;
    // Showing the emitted name is the point: this is a transpiler, and knowing what a member
    // becomes is as useful as knowing its type.
    item.documentation = new vscode.MarkdownString(`Emits \`${emitted}\``);
    return item;
  });
}

/** Hovering a mapped member shows what it transpiles to. */
function hoverProvider() {
  return {
    provideHover(document, position) {
      const range = document.getWordRangeAtPosition(position);
      if (!range) return undefined;

      const word = document.getText(range);
      const mapping = TRANSLATIONS[word];
      if (!mapping) return undefined;

      const markdown = new vscode.MarkdownString();
      markdown.appendMarkdown(`**CSJS** — emits:\n\n`);
      markdown.appendCodeblock(mapping, 'javascript');
      return new vscode.Hover(markdown, range);
    },
  };
}

/** What the most commonly asked-about constructs turn into. */
const TRANSLATIONS = {
  List: '[]  // a real Array',
  Dictionary: 'new Map()',
  HashSet: 'new Set()',
  Task: 'Promise',
  IEnumerable: 'Array, or a generator when produced by an iterator',
  StringBuilder: 'csjsStringBuilder()  // runtime helper',
  JsExport: 'export class …  (plus globalThis assignment when Global = true)',
  JsImport: "import { … } from '…'",
  JsGlobal: 'the JavaScript global itself; nothing is declared',
  JsObjectLiteral: '{ … }  // a plain object, no class',
};

function deactivate() {
  diagnostics?.clear();
}

module.exports = { activate, deactivate };
