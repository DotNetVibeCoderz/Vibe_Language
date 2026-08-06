# Debugging bridge

CSJS emits a v3 source map beside every module, so a breakpoint set in a `.csjs` file stops on
the corresponding C# line even though the browser is running JavaScript. Nothing CSJS-specific
is needed at debug time — the map is what does the work, and VS Code's built-in JavaScript
debugger reads it.

Two things have to be true for it to work:

1. `sourceMaps` is enabled in `csjs.json` (it is by default).
2. The C# sources sit next to the emitted output, which `cs2js build` handles by copying them
   into the output directory.

## launch.json

Add this to `.vscode/launch.json` in your project:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "CSJS: debug in Chrome",
      "type": "chrome",
      "request": "launch",
      "url": "http://localhost:5080",
      "webRoot": "${workspaceFolder}/dist",
      "preLaunchTask": "csjs: build",
      "sourceMapPathOverrides": {
        "*": "${workspaceFolder}/dist/*"
      }
    },
    {
      "name": "CSJS: debug on Node",
      "type": "node",
      "request": "launch",
      "program": "${workspaceFolder}/dist/Program.js",
      "preLaunchTask": "csjs: build",
      "sourceMaps": true,
      "outFiles": ["${workspaceFolder}/dist/**/*.js"]
    }
  ]
}
```

## tasks.json

The extension contributes a `csjs` task type, so builds are available to `preLaunchTask` and
their diagnostics land in the Problems panel:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "type": "csjs",
      "command": "build",
      "problemMatcher": "$csjs",
      "group": { "kind": "build", "isDefault": true },
      "label": "csjs: build"
    },
    {
      "type": "csjs",
      "command": "serve",
      "isBackground": true,
      "problemMatcher": "$csjs",
      "label": "csjs: serve"
    }
  ]
}
```

## What stepping looks like

Stepping through transpiled code lands on C# lines, but the values you inspect are JavaScript
values. That difference is visible in a few places and is worth knowing about:

- A `List<T>` shows as an Array, a `Dictionary<K,V>` as a Map.
- A `long` shows as a BigInt, so the debugger prints `42n` rather than `42`.
- An iterator method is a generator, so its locals only exist while it is suspended mid-yield.
- An `enum` value shows as its number. Its name is available as `EnumName[value]`.
