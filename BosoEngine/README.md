# BosoEngine

**English**

BosoEngine is a simple tool to transpile, run, and compile Boso Script into C#.

## Features
- **Transpile** Boso Script to C# using Roslyn validation
- **Run** Boso Script using `dotnet`
- **Compile** Boso Script to executable using `dotnet`

## Usage

```bash
boso transpile <file.boso> [-o output.cs]
boso run <file.boso>
boso compile <file.boso> [-o output.exe]
```

## Examples
```bash
boso transpile samples/hello.boso -o out/hello.cs
boso run samples/hello.boso
boso compile samples/hello.boso -o out/hello.exe
```

---

**Bahasa Indonesia**

BosoEngine adalah tool sederhana untuk transpile, menjalankan, dan meng-compile Boso Script ke C#.

## Fitur
- **Transpile** Boso Script ke C# dengan validasi Roslyn
- **Run** Boso Script menggunakan `dotnet`
- **Compile** Boso Script menjadi executable dengan `dotnet`

## Cara Pakai

```bash
boso transpile <file.boso> [-o output.cs]
boso run <file.boso>
boso compile <file.boso> [-o output.exe]
```

## Contoh
```bash
boso transpile samples/hello.boso -o out/hello.cs
boso run samples/hello.boso
boso compile samples/hello.boso -o out/hello.exe
```
