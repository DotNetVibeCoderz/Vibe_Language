using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSJS.Core;

namespace CSJS.Cli;

/// <summary>Build switches that are not project settings: they come from the command line.</summary>
public sealed class BuildSettings
{
    /// <summary>Write a single bundle instead of (or, with <see cref="KeepModules"/>, beside) the modules.</summary>
    public bool Bundle { get; set; }

    /// <summary>Minify the bundle. Implies <see cref="Bundle"/>.</summary>
    public bool Minify { get; set; }

    /// <summary>Also write the individual modules when bundling.</summary>
    public bool KeepModules { get; set; }

    /// <summary>Bundle file name, relative to the output directory.</summary>
    public string BundleFile { get; set; } = "bundle.js";

    /// <summary>Ignore the cache stamp and the in-process session: a full, cold build.</summary>
    public bool Force { get; set; }

    /// <summary>Settings from the command line layered over the project's <c>bundle</c> section.</summary>
    public static BuildSettings From(CsjsProject project, string[] args)
    {
        var bundle = project.Bundle;
        var settings = new BuildSettings
        {
            Bundle = bundle?.Enabled == true || args.Contains("--bundle") || args.Contains("--minify"),
            Minify = bundle?.Minify == true || args.Contains("--minify"),
            KeepModules = bundle?.Modules == true,
            BundleFile = bundle?.File is { Length: > 0 } file ? file : "bundle.js",
            Force = args.Contains("--force") || args.Contains("--no-incremental"),
        };
        return settings;
    }

    /// <summary>Everything here that changes what a build writes, for the cache key.</summary>
    internal string Fingerprint => $"bundle={Bundle};minify={Minify};modules={KeepModules};file={BundleFile}";
}

/// <summary>
/// Writes output files only when their content changed, and remembers every file it was
/// responsible for so the cache stamp can check they are still there.
/// <para>
/// Rewriting an identical file is not free even when it is fast: it bumps the timestamp, which
/// wakes every file watcher downstream — a dev server, a test runner, an Electron reloader — for
/// nothing.
/// </para>
/// </summary>
public sealed class OutputWriter
{
    private readonly List<string> _outputs = new();

    public int Written { get; private set; }
    public int Unchanged { get; private set; }

    /// <summary>Every file written or confirmed current, full paths.</summary>
    public IReadOnlyList<string> Outputs => _outputs;

    public void Write(string path, string content)
    {
        _outputs.Add(Path.GetFullPath(path));

        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            // A length check first keeps the common "changed" case from reading the old file.
            if (info.Length == Encoding.UTF8.GetByteCount(content) && File.ReadAllText(path) == content)
            {
                Unchanged++;
                return;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, content);
        Written++;
    }

    /// <summary>Records a file produced some other way, such as a copied asset.</summary>
    public void Track(string path) => _outputs.Add(Path.GetFullPath(path));
}

/// <summary>
/// The stamp that lets <c>cs2js build</c> skip a build that would change nothing.
/// <para>
/// The key hashes everything that decides the output: the tool itself (the module version ids of
/// the CLI and the engine, so a rebuilt transpiler never trusts an old stamp), the project file,
/// the command-line switches, each source's path and content, the extra references and assets by
/// size and timestamp. A build is skipped only when the key matches <i>and</i> every output the
/// stamp lists is still on disk exactly as that build left it — deleting <c>dist</c> rebuilds.
/// </para>
/// </summary>
public sealed class BuildCache
{
    private const string StampVersion = "1";

    private readonly string _stampPath;

    public BuildCache(string projectDirectory)
        => _stampPath = Path.Combine(Path.GetFullPath(projectDirectory), "obj", "csjs", "build.stamp.json");

    /// <summary>Directory holding the stamp; excluded from asset copying.</summary>
    public static string CacheDirectory(string projectDirectory)
        => Path.Combine(Path.GetFullPath(projectDirectory), "obj", "csjs");

    public string StampPath => _stampPath;

    /// <summary>What the stamp remembers about the build that wrote it.</summary>
    public sealed class Stamp
    {
        public string Version { get; set; } = StampVersion;
        public string Key { get; set; } = "";
        public int ModuleCount { get; set; }
        public List<CsjsDiagnostic> Warnings { get; set; } = new();
        public List<OutputEntry> Outputs { get; set; } = new();
    }

    public sealed record OutputEntry(string Path, long Length, long Ticks);

    public static string ComputeKey(
        CsjsProject project,
        string projectDirectory,
        IReadOnlyList<SourceDocument> documents,
        TranspileOptions options,
        BuildSettings settings)
    {
        var sb = new StringBuilder();
        sb.Append("stamp:").Append(StampVersion).Append('\n');
        sb.Append("cli:").Append(typeof(BuildCache).Assembly.ManifestModule.ModuleVersionId).Append('\n');
        sb.Append("core:").Append(typeof(Transpiler).Assembly.ManifestModule.ModuleVersionId).Append('\n');
        sb.Append("project:").Append(JsonSerializer.Serialize(project)).Append('\n');
        sb.Append("settings:").Append(settings.Fingerprint).Append('\n');
        sb.Append("options:").Append(options.Module).Append(options.EmitSourceMap).Append(options.EmitTypeDefinitions)
          .Append(options.EmitEntryPointCall).Append(options.StrictWarnings).Append(options.CheckOverflow)
          .Append(options.RemoveUnusedPrivateMembers).Append(options.InlineEnums).Append(options.AssemblyName)
          .Append(string.Join(",", options.DefineConstants)).Append('\n');

        foreach (var document in documents)
        {
            sb.Append("src:").Append(document.Path).Append(':')
              .Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.Text)))).Append('\n');
        }

        foreach (var reference in options.AdditionalReferences) AppendFile(sb, "ref", reference);
        foreach (var (source, relative) in project.ResolveAssets(projectDirectory)) AppendFile(sb, "asset:" + relative, source);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static void AppendFile(StringBuilder sb, string label, string path)
    {
        var info = new FileInfo(path);
        sb.Append(label).Append(':').Append(path).Append(':')
          .Append(info.Exists ? info.Length : -1).Append(':')
          .Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0).Append('\n');
    }

    /// <summary>The stamp, when it matches <paramref name="key"/> and its outputs are intact.</summary>
    public Stamp? TryUpToDate(string key)
    {
        Stamp? stamp;
        try
        {
            if (!File.Exists(_stampPath)) return null;
            stamp = JsonSerializer.Deserialize<Stamp>(File.ReadAllText(_stampPath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }

        if (stamp is null || stamp.Version != StampVersion || stamp.Key != key) return null;

        foreach (var output in stamp.Outputs)
        {
            var info = new FileInfo(output.Path);
            if (!info.Exists || info.Length != output.Length || info.LastWriteTimeUtc.Ticks != output.Ticks)
                return null;
        }

        return stamp;
    }

    public void Save(string key, int moduleCount, IEnumerable<CsjsDiagnostic> warnings, IEnumerable<string> outputs)
    {
        var stamp = new Stamp
        {
            Key = key,
            ModuleCount = moduleCount,
            Warnings = warnings.ToList(),
            Outputs = outputs
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .Select(p => new FileInfo(p))
                .Select(i => new OutputEntry(i.FullName, i.Length, i.LastWriteTimeUtc.Ticks))
                .ToList(),
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stampPath)!);
            File.WriteAllText(_stampPath, JsonSerializer.Serialize(stamp));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stamp that cannot be written only costs the next build its shortcut.
        }
    }

    /// <summary>Forgets the stamp, so the next build runs in full.</summary>
    public void Invalidate()
    {
        try { File.Delete(_stampPath); } catch (IOException) { }
    }
}
