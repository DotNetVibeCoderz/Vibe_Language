using System.Text.Json;
using CSJS.Core;
using CSJSGen.Models;

namespace CSJSGen.Services;

/// <summary>A file in the project explorer.</summary>
public sealed class ProjectFile
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public string Content { get; set; } = "";
    public bool IsDirty { get; set; }

    /// <summary>True for a file the transpiler will process.</summary>
    public bool IsCsjs => Name.EndsWith(".csjs", StringComparison.OrdinalIgnoreCase)
                          || Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
}

/// <summary>An open CSJS project.</summary>
public sealed class Project
{
    public required string Name { get; set; }
    public required string Directory { get; set; }
    public List<ProjectFile> Files { get; } = new();

    public string ConfigPath => System.IO.Path.Combine(Directory, "csjs.json");
    public bool HasUnsavedChanges => Files.Any(f => f.IsDirty);
}

/// <summary>Result of compiling the current project or file.</summary>
/// <param name="Success">False when any error was reported.</param>
/// <param name="Diagnostics">Everything the transpiler said.</param>
/// <param name="JavaScript">The emitted JavaScript, for the output panel.</param>
/// <param name="TypeDefinitions">The emitted declarations, when requested.</param>
public sealed record CompileResult(
    bool Success,
    IReadOnlyList<CsjsDiagnostic> Diagnostics,
    string JavaScript,
    string? TypeDefinitions);

/// <summary>
/// Project lifecycle for the editor: new, open, save, save-as, close — applied both to whole
/// projects and to individual <c>.csjs</c> files, as the spec requires.
/// </summary>
public sealed class ProjectService
{
    /// <summary>The open project, or null when only a loose file is being edited.</summary>
    public Project? Current { get; private set; }

    /// <summary>The file shown in the editor.</summary>
    public ProjectFile? ActiveFile { get; private set; }

    public event Action? Changed;

    // ---------------------------------------------------------------------------------------
    // Project lifecycle
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a project on disk from a template.
    /// <para>
    /// The template supplies the source and host page; the project file and .gitignore are the
    /// same for every project, so they are written here rather than duplicated per template.
    /// </para>
    /// </summary>
    public Project NewProject(string directory, string name, string templateId = "blank")
    {
        var template = ProjectTemplates.Find(templateId)
                       ?? ProjectTemplates.Find("blank")
                       ?? throw new InvalidOperationException("No project templates are available.");

        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "csjs.json"), ProjectTemplates.ConfigFor(name));
        File.WriteAllText(
            Path.Combine(directory, ".gitignore"),
            string.Join(Environment.NewLine, "dist/", "bin/", "obj/", "node_modules/"));

        foreach (var file in template.Files)
        {
            var path = Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Content);
        }

        return OpenProject(directory);
    }

    /// <summary>Opens a project directory and loads its source files.</summary>
    public Project OpenProject(string directory)
    {
        var name = new DirectoryInfo(directory).Name;

        if (File.Exists(Path.Combine(directory, "csjs.json")))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "csjs.json")));
                if (document.RootElement.TryGetProperty("name", out var configured)
                    && configured.GetString() is { Length: > 0 } configuredName)
                {
                    name = configuredName;
                }
            }
            catch
            {
                // A malformed config should not block opening; the directory name is a fine fallback.
            }
        }

        var project = new Project { Name = name, Directory = directory };

        foreach (var path in EnumerateSourceFiles(directory))
        {
            project.Files.Add(new ProjectFile
            {
                Path = path,
                Name = Path.GetRelativePath(directory, path),
                Content = File.ReadAllText(path),
            });
        }

        Current = project;
        ActiveFile = project.Files.FirstOrDefault(f => f.IsCsjs) ?? project.Files.FirstOrDefault();
        Changed?.Invoke();
        return project;
    }

    /// <summary>Closes the project, discarding in-memory state.</summary>
    public void CloseProject()
    {
        Current = null;
        ActiveFile = null;
        Changed?.Invoke();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directory)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", "dist", ".git", ".vs",
        };

        foreach (var pattern in new[] { "*.csjs", "*.cs", "*.html", "*.css", "*.json" })
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(directory, path);
                var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(skip.Contains)) continue;
                yield return path;
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // File lifecycle
    // ---------------------------------------------------------------------------------------

    public void SelectFile(string path)
    {
        var file = Current?.Files.FirstOrDefault(f => f.Path == path);
        if (file is null) return;
        ActiveFile = file;
        Changed?.Invoke();
    }

    /// <summary>Opens a single file with no surrounding project.</summary>
    public ProjectFile OpenFile(string path)
    {
        var file = new ProjectFile
        {
            Path = path,
            Name = Path.GetFileName(path),
            Content = File.ReadAllText(path),
        };

        // A loose file still needs somewhere to live in the explorer.
        Current ??= new Project { Name = "(tanpa proyek)", Directory = Path.GetDirectoryName(path) ?? "" };
        if (Current.Files.All(f => f.Path != path)) Current.Files.Add(file);

        ActiveFile = file;
        Changed?.Invoke();
        return file;
    }

    /// <summary>Creates a new file inside the open project.</summary>
    public ProjectFile NewFile(string name)
    {
        if (Current is null) throw new InvalidOperationException("No project is open.");

        var directory = Directory.Exists(Path.Combine(Current.Directory, "src"))
            ? Path.Combine(Current.Directory, "src")
            : Current.Directory;

        var path = Path.Combine(directory, name);
        var file = new ProjectFile
        {
            Path = path,
            Name = Path.GetRelativePath(Current.Directory, path),
            Content = $"// {name}\n",
            IsDirty = true,
        };

        Current.Files.Add(file);
        ActiveFile = file;
        Changed?.Invoke();
        return file;
    }

    public void UpdateActiveContent(string content)
    {
        if (ActiveFile is null) return;
        if (ActiveFile.Content == content) return;

        ActiveFile.Content = content;
        ActiveFile.IsDirty = true;
        Changed?.Invoke();
    }

    public void SaveActive()
    {
        if (ActiveFile is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ActiveFile.Path)!);
        File.WriteAllText(ActiveFile.Path, ActiveFile.Content);
        ActiveFile.IsDirty = false;
        Changed?.Invoke();
    }

    public void SaveActiveAs(string path)
    {
        if (ActiveFile is null) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ActiveFile.Content);

        var saved = new ProjectFile
        {
            Path = path,
            Name = Current is null ? Path.GetFileName(path) : Path.GetRelativePath(Current.Directory, path),
            Content = ActiveFile.Content,
        };

        Current?.Files.Add(saved);
        ActiveFile = saved;
        Changed?.Invoke();
    }

    public void SaveAll()
    {
        if (Current is null) return;
        foreach (var file in Current.Files.Where(f => f.IsDirty))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
            File.WriteAllText(file.Path, file.Content);
            file.IsDirty = false;
        }
        Changed?.Invoke();
    }

    public void CloseFile(string path)
    {
        var file = Current?.Files.FirstOrDefault(f => f.Path == path);
        if (file is null || Current is null) return;

        Current.Files.Remove(file);
        if (ActiveFile?.Path == path) ActiveFile = Current.Files.FirstOrDefault();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------------------------------
    // Compile and run
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Transpiles the whole project when one is open, or just the active file otherwise.
    /// Compiling the project rather than the file is what surfaces cross-module errors.
    /// </summary>
    public CompileResult Compile(bool emitTypeDefinitions = true)
    {
        var options = new TranspileOptions
        {
            EmitSourceMap = false,
            EmitTypeDefinitions = emitTypeDefinitions,
        };

        var documents = Current is not null && Current.Files.Any(f => f.IsCsjs)
            ? Current.Files.Where(f => f.IsCsjs)
                .Select(f => new SourceDocument(f.Name, f.Content))
                .ToList()
            : ActiveFile is not null
                ? new List<SourceDocument> { new(ActiveFile.Name, ActiveFile.Content) }
                : new List<SourceDocument>();

        if (documents.Count == 0)
        {
            return new CompileResult(false,
                new[] { new CsjsDiagnostic("CSJS0101", DiagnosticSeverity.Error, "There is nothing to compile.") },
                "", null);
        }

        var result = new Transpiler(options).Transpile(documents);

        // The output panel shows the active file's module when there is one, so what is
        // displayed corresponds to what the editor has open.
        var module = result.Modules.FirstOrDefault(m => ActiveFile is not null && m.SourcePath == ActiveFile.Name)
                     ?? result.Modules.FirstOrDefault();

        return new CompileResult(
            result.Success,
            result.Diagnostics,
            module?.JavaScript ?? "",
            module?.TypeDefinitions);
    }

}
