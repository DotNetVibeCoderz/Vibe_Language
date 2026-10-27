using CSJS.LanguageService;

namespace CSJS.Cli;

/// <summary>One CSJS project as the language server sees it.</summary>
/// <param name="Directory">The folder holding <c>csjs.json</c>, or a loose file's own folder.</param>
/// <param name="Service">The language service holding the project's documents.</param>
/// <param name="HasConfig">False for a loose file with no <c>csjs.json</c> above it.</param>
internal sealed record LspProject(string Directory, CsjsLanguageService Service, bool HasConfig);

/// <summary>
/// Maps documents to projects. A document belongs to the nearest <c>csjs.json</c> above it, and
/// that project is loaded the way <c>cs2js build</c> loads it — <see cref="CsjsProject"/>
/// resolves the sources, defines, and references — so the editor binds exactly what the build
/// binds. A file with no project above it is served alone.
/// </summary>
internal sealed class LspProjects : IDisposable
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly object _gate = new();
    private readonly Dictionary<string, LspProject> _projects = new(PathComparer);
    private readonly Dictionary<string, string> _openDocuments = new(PathComparer);

    /// <summary>Every loaded project.</summary>
    public IReadOnlyList<LspProject> All
    {
        get { lock (_gate) return _projects.Values.ToList(); }
    }

    /// <summary>The project a document belongs to, loading it on first use.</summary>
    public LspProject ForDocument(string path)
    {
        var directory = FindProjectDirectory(path);
        var hasConfig = directory is not null;
        directory = Key(directory ?? Path.GetDirectoryName(Path.GetFullPath(path)) ?? Path.GetFullPath(path));

        lock (_gate)
        {
            if (_projects.TryGetValue(directory, out var existing)) return existing;

            var project = Load(directory, hasConfig);
            _projects[directory] = project;
            return project;
        }
    }

    /// <summary>The loaded project holding a document, without loading anything.</summary>
    public LspProject? Find(string path)
    {
        lock (_gate)
        {
            return _projects.Values.FirstOrDefault(p => p.Service.Contains(path));
        }
    }

    /// <summary>Loads a project eagerly, e.g. the workspace root at <c>initialize</c>.</summary>
    public LspProject? LoadIfProject(string directory)
    {
        var full = Key(directory);
        if (!File.Exists(Path.Combine(full, CsjsProject.FileName))) return null;

        lock (_gate)
        {
            if (_projects.TryGetValue(full, out var existing)) return existing;
            var project = Load(full, hasConfig: true);
            _projects[full] = project;
            return project;
        }
    }

    public void MarkOpen(string path, string uri)
    {
        lock (_gate) _openDocuments[Path.GetFullPath(path)] = uri;
    }

    public void MarkClosed(string path)
    {
        lock (_gate) _openDocuments.Remove(Path.GetFullPath(path));
    }

    public bool IsOpen(string path)
    {
        lock (_gate) return _openDocuments.ContainsKey(Path.GetFullPath(path));
    }

    /// <summary>The URI a client used for a path, so diagnostics go back under the same spelling.</summary>
    public string UriFor(string path)
    {
        lock (_gate)
        {
            if (_openDocuments.TryGetValue(Path.GetFullPath(path), out var uri)) return uri;
        }

        return new Uri(Path.GetFullPath(path)).AbsoluteUri;
    }

    /// <summary>True when the project's <c>csjs.json</c> includes this file as a source.</summary>
    public static bool IsProjectSource(LspProject project, string path)
    {
        if (!project.HasConfig) return false;
        var config = CsjsProject.Load(project.Directory);
        return config.ResolveSources(project.Directory).Contains(Path.GetFullPath(path), PathComparer);
    }

    /// <summary>
    /// Drops a project so the next request reloads it — after its <c>csjs.json</c> changed.
    /// Open documents are carried over by the caller, which still holds their text.
    /// </summary>
    public void Unload(string directory)
    {
        lock (_gate)
        {
            if (_projects.Remove(Key(directory), out var project))
                project.Service.Dispose();
        }
    }

    private static string Key(string directory) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    private static LspProject Load(string directory, bool hasConfig)
    {
        var options = new LanguageServiceOptions();
        List<string> sources = new();

        if (hasConfig)
        {
            var config = CsjsProject.Load(directory);
            options.AssemblyName = config.Name ?? "CsjsModule";
            options.DefineConstants.AddRange(config.Define);
            options.AdditionalReferences.AddRange(
                config.References.Select(r => Path.GetFullPath(Path.Combine(directory, r))));

            try
            {
                sources = config.ResolveSources(directory);
            }
            catch (IOException)
            {
                // An unreadable tree still leaves the open documents to work with.
            }
        }

        var service = new CsjsLanguageService(options);
        foreach (var source in sources)
        {
            try
            {
                service.UpdateDocument(source, File.ReadAllText(source));
            }
            catch (IOException)
            {
                // A file locked by another process is skipped; it is picked up when opened.
            }
        }

        return new LspProject(directory, service, hasConfig);
    }

    /// <summary>The nearest folder at or above the document holding <c>csjs.json</c>.</summary>
    private static string? FindProjectDirectory(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "");
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, CsjsProject.FileName)))
                return directory.FullName;
        }

        return null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var project in _projects.Values) project.Service.Dispose();
            _projects.Clear();
        }
    }
}
