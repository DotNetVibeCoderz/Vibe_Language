using System.Diagnostics;
using CSJS.Core;

namespace CSJS.Cli;

/// <summary>Outcome of one build, kept small so watch mode can report it repeatedly.</summary>
public sealed partial record BuildReport(bool Success, int ModuleCount, int ErrorCount, int WarningCount, TimeSpan Duration);

/// <summary>Drives the transpiler over a <see cref="CsjsProject"/> and writes the results to disk.</summary>
public sealed partial class BuildRunner
{
    private readonly CsjsProject _project;
    private readonly string _projectDirectory;
    private readonly bool _emitEntryPointCall;

    public BuildRunner(CsjsProject project, string projectDirectory, bool emitEntryPointCall = false, BuildSettings? settings = null)
    {
        _project = project;
        _projectDirectory = projectDirectory;
        _emitEntryPointCall = emitEntryPointCall;
        _settings = settings ?? new BuildSettings();
    }

    public BuildReport Run(bool quiet = false)
    {
        var stopwatch = Stopwatch.StartNew();

        var sourceRoot = _project.ResolveSourceRoot(_projectDirectory);
        var outputRoot = _project.ResolveOutputRoot(_projectDirectory);
        var files = _project.ResolveSources(_projectDirectory);

        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No source files found under {sourceRoot}.");
            return new BuildReport(false, 0, 1, 0, stopwatch.Elapsed);
        }

        var documents = files
            .Select(f => new SourceDocument(Path.GetRelativePath(sourceRoot, f), File.ReadAllText(f)))
            .ToList();

        var options = new TranspileOptions
        {
            Module = _project.Module.Equals("script", StringComparison.OrdinalIgnoreCase)
                ? ModuleKind.Script
                : ModuleKind.ESM,
            EmitTypeDefinitions = _project.TypeDefinitions,
            EmitSourceMap = _project.SourceMaps,
            StrictWarnings = _project.StrictWarnings,
            AssemblyName = _project.Name ?? "CsjsModule",
            EmitEntryPointCall = _emitEntryPointCall,
        };
        options.DefineConstants.AddRange(_project.Define);
        options.AdditionalReferences.AddRange(
            _project.References.Select(r => Path.GetFullPath(Path.Combine(_projectDirectory, r))));

        // CSJS.Testing ships with the CLI, so a project can write tests without configuring a
        // reference for something the toolchain already carries.
        options.AdditionalReferences.Add(typeof(CSJS.Testing.Assert).Assembly.Location);
        // CSJS.Web — the whole web platform as typed C# — ships with the CLI the same way.
        options.AdditionalReferences.Add(typeof(CSJS.Web.Dom).Assembly.Location);

        if (TryUpToDate(documents, options, sourceRoot, quiet, stopwatch) is { } upToDate) return upToDate;

        var result = Transpile(options, documents);

        foreach (var diagnostic in result.Diagnostics.OrderByDescending(d => d.Severity))
            WriteDiagnostic(diagnostic, sourceRoot);

        var errorCount = result.Errors.Count();
        var warningCount = result.Warnings.Count();

        if (!result.Success)
            return new BuildReport(false, 0, errorCount, warningCount, stopwatch.Elapsed) { Diagnostics = result.Diagnostics };

        Directory.CreateDirectory(outputRoot);
        var writer = new OutputWriter();

        foreach (var module in WritesModules ? result.Modules : [])
        {
            // Output mirrors the source tree so relative imports between modules stay valid.
            var relativeDirectory = Path.GetDirectoryName(module.SourcePath) ?? "";
            var targetDirectory = Path.Combine(outputRoot, relativeDirectory);
            Directory.CreateDirectory(targetDirectory);

            var jsPath = Path.Combine(targetDirectory, module.FileName);
            writer.Write(jsPath, module.JavaScript);

            if (module.SourceMap is not null)
                writer.Write(jsPath + ".map", RootedSourceMap(module.SourceMap, targetDirectory, Path.Combine(sourceRoot, relativeDirectory)));

            if (module.TypeDefinitions is not null)
                writer.Write(Path.ChangeExtension(jsPath, ".d.ts"), module.TypeDefinitions);

            if (!quiet)
                Console.WriteLine($"  {Path.GetRelativePath(_projectDirectory, jsPath)}");
        }

        // The runtime module is only written when something actually imported a helper.
        if (result.RuntimeJavaScript is not null && WritesModules)
        {
            var runtimePath = Path.Combine(outputRoot, "csjs-runtime.js");
            writer.Write(runtimePath, result.RuntimeJavaScript);
            if (!quiet) Console.WriteLine($"  {Path.GetRelativePath(_projectDirectory, runtimePath)}");
        }

        var bundlePath = _settings.Bundle ? WriteBundle(result, outputRoot, sourceRoot, writer, quiet) : null;

        // The C# sources are deliberately *not* copied. Each map carries `sourcesContent`, so a
        // debugger already has the original text — shipping .csjs alongside the JavaScript would
        // publish the source without buying anything.

        // Assets last, so a stale copy never wins over something this build just emitted.
        var assetCount = 0;
        foreach (var (source, relative) in _project.ResolveAssets(_projectDirectory))
        {
            var target = Path.Combine(outputRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            writer.Track(target);

            // Unchanged files are skipped, which keeps watch mode from rewriting the whole tree
            // on every keystroke.
            if (File.Exists(target)
                && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source)
                && new FileInfo(target).Length == new FileInfo(source).Length)
                continue;

            File.Copy(source, target, overwrite: true);
            assetCount++;
        }

        if (assetCount > 0 && !quiet)
            Console.WriteLine($"  {assetCount} asset(s) copied.");

        SaveStamp(result.Modules.Count, result.Warnings, writer);

        stopwatch.Stop();
        return new BuildReport(true, result.Modules.Count, errorCount, warningCount, stopwatch.Elapsed)
        {
            FilesWritten = writer.Written,
            FilesUnchanged = writer.Unchanged,
            Diagnostics = result.Diagnostics,
            Incremental = _settings.Force ? null : _session?.LastRun,
            BundlePath = bundlePath,
        };
    }

    private static void WriteDiagnostic(CsjsDiagnostic diagnostic, string sourceRoot)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => ConsoleColor.Red,
            DiagnosticSeverity.Warning => ConsoleColor.Yellow,
            _ => previous,
        };

        var writer = diagnostic.Severity == DiagnosticSeverity.Error ? Console.Error : Console.Out;
        writer.WriteLine("  " + diagnostic);

        Console.ForegroundColor = previous;
    }
}
