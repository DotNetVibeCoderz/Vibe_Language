using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSJS.Cli;

/// <summary>
/// The <c>csjs.json</c> project file. Everything has a default, so an empty file — or no file at
/// all — still builds a conventional <c>src/</c> to <c>dist/</c> project.
/// </summary>
public sealed class CsjsProject
{
    /// <summary>Project display name. Defaults to the containing directory.</summary>
    public string? Name { get; set; }

    /// <summary>Directory holding <c>.csjs</c> / <c>.cs</c> sources, relative to the project file.</summary>
    public string Source { get; set; } = "src";

    /// <summary>Directory that receives emitted JavaScript.</summary>
    public string Output { get; set; } = "dist";

    /// <summary>Glob-ish include patterns. Extensions only; simple and predictable.</summary>
    public List<string> Include { get; set; } = new() { "*.csjs", "*.cs" };

    /// <summary>Directory names skipped while scanning for sources.</summary>
    public List<string> Exclude { get; set; } = new() { "bin", "obj", "node_modules", "dist" };

    /// <summary>Module format: <c>esm</c> or <c>script</c>.</summary>
    public string Module { get; set; } = "esm";

    /// <summary>Emit <c>.d.ts</c> declarations next to each module.</summary>
    public bool TypeDefinitions { get; set; } = true;

    /// <summary>Emit source maps, which the VSCode debug bridge needs.</summary>
    public bool SourceMaps { get; set; } = true;

    /// <summary>Treat C# warnings as errors.</summary>
    public bool StrictWarnings { get; set; }

    /// <summary>Preprocessor symbols passed to the C# parser.</summary>
    public List<string> Define { get; set; } = new();

    /// <summary>Extra assembly paths to reference when binding.</summary>
    public List<string> References { get; set; } = new();

    /// <summary>HTML file served by <c>cs2js serve</c>. Defaults to <c>index.html</c> when present.</summary>
    public string? Html { get; set; }

    /// <summary>
    /// Copy everything that is not a source file — stylesheets, images, media, fonts, data,
    /// <c>.env</c>, plain text — into the output, keeping the folder structure it had. Without
    /// this the emitted JavaScript is served on its own and every asset the page references
    /// 404s, which looks like a transpiler failure but is not one.
    /// </summary>
    public bool Assets { get; set; } = true;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public const string FileName = "csjs.json";

    /// <summary>Loads <c>csjs.json</c> from a directory, or returns defaults when absent.</summary>
    public static CsjsProject Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new CsjsProject { Name = new DirectoryInfo(directory).Name };

        var json = File.ReadAllText(path);
        var project = JsonSerializer.Deserialize<CsjsProject>(json, SerializerOptions) ?? new CsjsProject();
        project.Name ??= new DirectoryInfo(directory).Name;
        return project;
    }

    public void Save(string directory)
        => File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(this, SerializerOptions));

    /// <summary>Finds every source file this project includes, in a stable order.</summary>
    public List<string> ResolveSources(string projectDirectory)
    {
        var root = Path.GetFullPath(Path.Combine(projectDirectory, Source));
        if (!Directory.Exists(root))
        {
            // A flat project with sources beside csjs.json is a reasonable shape too.
            root = Path.GetFullPath(projectDirectory);
        }

        var excluded = new HashSet<string>(Exclude, StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();

        foreach (var pattern in Include)
        {
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(excluded.Contains)) continue;
                results.Add(file);
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Every non-source file the build should carry into the output, paired with the relative
    /// path it keeps there.
    ///
    /// Two roots, because the output has two kinds of neighbour. An asset beside the sources
    /// belongs beside the emitted modules, so it stays relative to the source root — the same
    /// rule the emitted JavaScript follows. Everything else keeps its path relative to the
    /// project, which is what the host page's own <c>href</c>s already assume.
    /// </summary>
    public List<(string Source, string Relative)> ResolveAssets(string projectDirectory)
    {
        if (!Assets) return new List<(string, string)>();

        var projectRoot = Path.GetFullPath(projectDirectory);
        var sourceRoot = ResolveSourceRoot(projectDirectory);
        var outputRoot = ResolveOutputRoot(projectDirectory);

        var excluded = new HashSet<string>(Exclude, StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".idea", ".vscode", "artifacts",
        };

        // Sources are emitted, not copied — the build already places them and their maps.
        var sourceExtensions = new HashSet<string>(
            Include.Select(p => Path.GetExtension(p)).Where(e => e.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var assets = new List<(string, string)>();

        foreach (var file in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);

            // Never re-copy what a previous build emitted; that grows dist on every run.
            if (full.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            var fromProject = Path.GetRelativePath(projectRoot, full);
            var segments = fromProject.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(excluded.Contains)) continue;

            if (sourceExtensions.Contains(Path.GetExtension(full))) continue;
            if (Path.GetFileName(full).Equals(FileName, StringComparison.OrdinalIgnoreCase)) continue;

            var underSource = full.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            var relative = underSource ? Path.GetRelativePath(sourceRoot, full) : fromProject;

            assets.Add((full, relative));
        }

        return assets
            .DistinctBy(a => a.Item2, StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a.Item2, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string ResolveSourceRoot(string projectDirectory)
    {
        var root = Path.GetFullPath(Path.Combine(projectDirectory, Source));
        return Directory.Exists(root) ? root : Path.GetFullPath(projectDirectory);
    }

    public string ResolveOutputRoot(string projectDirectory)
        => Path.GetFullPath(Path.Combine(projectDirectory, Output));
}
