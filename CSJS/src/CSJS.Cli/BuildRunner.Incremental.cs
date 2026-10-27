using System.Diagnostics;
using CSJS.Core;
using CSJS.Core.Bundling;

namespace CSJS.Cli;

public sealed partial record BuildReport
{
    /// <summary>The cache stamp proved nothing changed, so nothing was transpiled or written.</summary>
    public bool UpToDate { get; init; }

    /// <summary>Output files whose content changed and were rewritten.</summary>
    public int FilesWritten { get; init; }

    /// <summary>Output files that already held exactly what this build produced.</summary>
    public int FilesUnchanged { get; init; }

    /// <summary>Everything the transpiler reported, for hosts that show it somewhere other than the console.</summary>
    public IReadOnlyList<CsjsDiagnostic> Diagnostics { get; init; } = Array.Empty<CsjsDiagnostic>();

    /// <summary>What an in-process session reused; null for a cold build.</summary>
    public TranspileSessionStats? Incremental { get; init; }

    /// <summary>Full path of the bundle, when one was written.</summary>
    public string? BundlePath { get; init; }

    /// <summary>One line for the console: what was built, how, and how long it took.</summary>
    public string Summary()
    {
        var warnings = WarningCount > 0 ? $", {WarningCount} warning(s)" : "";
        var time = $"{Duration.TotalMilliseconds:F0} ms";

        if (UpToDate) return $"Up to date: {ModuleCount} module(s), nothing to do ({time}){warnings}.";

        var reuse = Incremental is { Reused: > 0 } s ? $" ({s.Emitted} emitted, {s.Reused} reused)" : "";
        var files = FilesUnchanged > 0 ? $"; {FilesWritten} file(s) written, {FilesUnchanged} unchanged" : "";
        return $"Built {ModuleCount} module(s){reuse} in {time}{warnings}{files}.";
    }
}

/// <summary>
/// The incremental half of <see cref="BuildRunner"/>: the cache stamp that skips a build which
/// would change nothing, the session that keeps a watcher's rebuilds warm, and the bundle step.
/// </summary>
public sealed partial class BuildRunner
{
    private BuildSettings _settings = new();
    private TranspileSession? _session;
    private string? _stampKey;

    /// <summary>Command-line switches for this runner's builds.</summary>
    public BuildSettings Settings => _settings;

    /// <summary>Whether per-module files are written; a bundle-only build writes just the bundle.</summary>
    private bool WritesModules => !_settings.Bundle || _settings.KeepModules;

    /// <summary>
    /// Returns a report for a build that can be skipped, or null to build. Computes the key the
    /// stamp is saved under afterwards either way.
    /// </summary>
    private BuildReport? TryUpToDate(
        List<SourceDocument> documents, TranspileOptions options, string sourceRoot, bool quiet, Stopwatch stopwatch)
    {
        // A forced build skips the check but still leaves a stamp, so the next build can skip.
        _stampKey = BuildCache.ComputeKey(_project, _projectDirectory, documents, options, _settings);
        if (_settings.Force) return null;

        if (new BuildCache(_projectDirectory).TryUpToDate(_stampKey) is not { } stamp) return null;

        foreach (var warning in stamp.Warnings) WriteDiagnostic(warning, sourceRoot);
        if (!quiet) Console.WriteLine("  Up to date.");

        return new BuildReport(true, stamp.ModuleCount, 0, stamp.Warnings.Count, stopwatch.Elapsed)
        {
            UpToDate = true,
            Diagnostics = stamp.Warnings,
        };
    }

    /// <summary>
    /// Transpiles through the runner's session, so a runner kept alive by watch or serve rebuilds
    /// incrementally. <c>--force</c> uses a fresh transpiler every time.
    /// </summary>
    private TranspileResult Transpile(TranspileOptions options, List<SourceDocument> documents)
    {
        if (_settings.Force) return new Transpiler(options).Transpile(documents);

        _session ??= new TranspileSession(options);
        return _session.Transpile(documents);
    }

    private void SaveStamp(int moduleCount, IEnumerable<CsjsDiagnostic> warnings, OutputWriter writer)
    {
        if (_stampKey is null) return;
        new BuildCache(_projectDirectory).Save(_stampKey, moduleCount, warnings, writer.Outputs);
    }

    /// <summary>
    /// A module's map with <c>sourceRoot</c> pointing from the map's directory back at the
    /// sources. The emitter names sources by file name alone; without this, Node's stack traces
    /// and browser devtools would look for <c>dist/Program.csjs</c>, which does not exist.
    /// </summary>
    private static string RootedSourceMap(string map, string mapDirectory, string sourceDirectory)
        => SourceMapDocument.WithSourceRoot(map, RelativeUrl(mapDirectory, sourceDirectory));

    private static string RelativeUrl(string from, string to)
    {
        var relative = Path.GetRelativePath(from, to).Replace('\\', '/');
        if (relative == ".") return "";
        return Path.IsPathRooted(relative)
            ? new Uri(relative.TrimEnd('/') + "/").AbsoluteUri // another drive: only a URL reaches it
            : relative.TrimEnd('/') + "/";
    }

    /// <summary>Writes the bundle and its map to the output root.</summary>
    private string? WriteBundle(TranspileResult result, string outputRoot, string sourceRoot, OutputWriter writer, bool quiet)
    {
        var bundlePath = Path.GetFullPath(Path.Combine(outputRoot, _settings.BundleFile));
        var bundleDirectory = Path.GetDirectoryName(bundlePath)!;

        var bundle = Bundler.Bundle(result, new BundleOptions
        {
            FileName = Path.GetFileName(bundlePath),
            Minify = _settings.Minify,
            EmitSourceMap = _project.SourceMaps,
            SourceRoot = RelativeUrl(bundleDirectory, sourceRoot),
        });

        writer.Write(bundlePath, bundle.JavaScript);
        if (bundle.SourceMap is not null) writer.Write(bundlePath + ".map", bundle.SourceMap);

        if (!quiet)
        {
            var size = System.Text.Encoding.UTF8.GetByteCount(bundle.JavaScript);
            Console.WriteLine($"  {Path.GetRelativePath(_projectDirectory, bundlePath)} — {bundle.ModuleOrder.Count} module(s), "
                + $"{bundle.RuntimeDeclarations.Count} runtime declaration(s), {size:N0} bytes{(_settings.Minify ? ", minified" : "")}");
        }

        return bundlePath;
    }
}
