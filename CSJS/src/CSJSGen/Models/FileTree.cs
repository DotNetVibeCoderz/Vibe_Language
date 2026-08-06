using CSJSGen.Services;

namespace CSJSGen.Models;

/// <summary>
/// A node in the project explorer: either a folder or a file.
/// <para>
/// The tree is rebuilt from the flat file list whenever the project changes rather than being
/// kept in sync incrementally — a project holds tens of files, not thousands, and a derived
/// structure cannot drift from the thing it is derived from.
/// </para>
/// </summary>
public sealed class FileNode
{
    /// <summary>Name shown in the tree — the last path segment.</summary>
    public required string Name { get; init; }

    /// <summary>Path relative to the project root, used as the expand/collapse key.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Absolute path. Empty for folders, which have no file behind them.</summary>
    public string FullPath { get; init; } = "";

    public bool IsFolder { get; init; }

    /// <summary>Depth from the root, used for indentation.</summary>
    public int Depth { get; init; }

    /// <summary>Children, folders first then files, each alphabetical.</summary>
    public List<FileNode> Children { get; } = new();

    /// <summary>The project file behind this node, when it is a file.</summary>
    public ProjectFile? File { get; init; }

    /// <summary>Extension without the dot, upper-cased, for the type badge.</summary>
    public string Kind => IsFolder
        ? ""
        : System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();

    /// <summary>Which colour the badge takes: source side, web side, or neither.</summary>
    public string KindClass
    {
        get
        {
            if (IsFolder) return "";
            if (File?.IsCsjs == true) return "cs";

            return System.IO.Path.GetExtension(Name).ToLowerInvariant() switch
            {
                ".html" or ".css" or ".js" or ".ts" => "web",
                _ => "other",
            };
        }
    }

    /// <summary>Builds the tree for a project's files.</summary>
    public static List<FileNode> Build(Project project)
    {
        var root = new List<FileNode>();
        var folders = new Dictionary<string, FileNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in project.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var segments = file.Name.Split(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);

            var parent = root;
            var accumulated = "";

            // Every segment but the last is a folder, created on first sight.
            for (var i = 0; i < segments.Length - 1; i++)
            {
                accumulated = accumulated.Length == 0 ? segments[i] : accumulated + "/" + segments[i];

                if (!folders.TryGetValue(accumulated, out var folder))
                {
                    folder = new FileNode
                    {
                        Name = segments[i],
                        RelativePath = accumulated,
                        IsFolder = true,
                        Depth = i,
                    };

                    folders[accumulated] = folder;
                    parent.Add(folder);
                }

                parent = folder.Children;
            }

            parent.Add(new FileNode
            {
                Name = segments[^1],
                RelativePath = file.Name.Replace('\\', '/'),
                FullPath = file.Path,
                Depth = segments.Length - 1,
                File = file,
            });
        }

        Sort(root);
        return root;
    }

    /// <summary>Folders before files, each alphabetical — the order every file explorer uses.</summary>
    private static void Sort(List<FileNode> nodes)
    {
        nodes.Sort((a, b) =>
        {
            if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var node in nodes.Where(n => n.IsFolder))
            Sort(node.Children);
    }

    /// <summary>Every folder path in the tree, so a project can start fully expanded.</summary>
    public static IEnumerable<string> FolderPaths(IEnumerable<FileNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.IsFolder) continue;
            yield return node.RelativePath;

            foreach (var nested in FolderPaths(node.Children))
                yield return nested;
        }
    }
}
