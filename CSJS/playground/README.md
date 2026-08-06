# CSJS Playground

The transpiler running in the browser. C# on the left, the JavaScript it becomes on the right.

![Playground](../docs/images/playground.png)

```bash
dotnet run --project playground
```

Then open the URL it prints.

## How it works

This is the same `CSJS.Core` assembly the CLI uses, running under WebAssembly. Two constraints
shaped it, and both are worth knowing if you touch this project:

**Single-threaded.** Roslyn's concurrent build waits on monitors, which WebAssembly cannot do.
The compilation is created with `concurrentBuild: false`.

**No filesystem.** Reference assemblies cannot be loaded by path, so `ReferenceLoader` downloads
them from `_framework/` and hands them to the transpiler as raw bytes via
`TranspileOptions.ReferenceImages`. That in turn means WebCIL has to be disabled
(`<WasmEnableWebcil>false</WasmEnableWebcil>`), because Roslyn reads PE files, not WebAssembly
modules.

## Publishing

```bash
dotnet publish playground -c Release
```

The output under `bin/Release/net10.0/publish/wwwroot` is a static site — no server required.

---

Gravicode Studios — led by Kang Fadhil.
