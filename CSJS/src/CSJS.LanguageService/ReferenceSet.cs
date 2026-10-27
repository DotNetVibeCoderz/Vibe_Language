using System.Collections.Immutable;
using System.Runtime.InteropServices;
using CSJS;
using Microsoft.CodeAnalysis;

namespace CSJS.LanguageService;

/// <summary>
/// The assemblies user code binds against — the same set <c>Transpiler.BuildReferences</c> and
/// <c>BuildRunner</c> use: the host's trusted platform assemblies, CSJS.Interop, CSJS.Testing,
/// and whatever the project adds. If the editor bound against anything else it would offer
/// members the build then rejects, or flag code the build accepts.
/// <para>
/// The one addition is documentation. The host's implementation assemblies carry no XML docs,
/// but the targeting pack beside the runtime does, and attaching it changes nothing about
/// binding — it only gives hover and completion something to say about BCL members.
/// </para>
/// </summary>
internal static class ReferenceSet
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> HostReferences = new(BuildHost);

    /// <summary>Host references plus the project's own extra assemblies.</summary>
    public static ImmutableArray<MetadataReference> For(IEnumerable<string> additional)
    {
        var host = HostReferences.Value;
        var seen = new HashSet<string>(
            host.OfType<PortableExecutableReference>().Select(r => Path.GetFileName(r.FilePath ?? "")),
            StringComparer.OrdinalIgnoreCase);

        var builder = host.ToBuilder();
        foreach (var path in additional)
        {
            if (!File.Exists(path) || !seen.Add(Path.GetFileName(path))) continue;
            builder.Add(CreateReference(path, null));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<MetadataReference> BuildHost()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var docs = FindReferencePackDocs();

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(Path.PathSeparator))
            {
                if (path.Length == 0 || !File.Exists(path)) continue;
                if (!seen.Add(Path.GetFileName(path))) continue;
                references.Add(CreateReference(path, docs));
            }
        }

        // CSJS.Interop carries the attributes and browser bindings; CSJS.Testing ships with the
        // CLI so a project can write tests without configuring a reference. Both are usually in
        // the TPA list already, which the name check accounts for.
        foreach (var assembly in new[] { typeof(JsGlobalAttribute).Assembly, typeof(CSJS.Testing.Assert).Assembly })
        {
            var path = assembly.Location;
            if (path.Length > 0 && seen.Add(Path.GetFileName(path)))
                references.Add(CreateReference(path, docs));
        }

        return references.ToImmutable();
    }

    private static MetadataReference CreateReference(string path, string? docsDirectory)
    {
        var xml = Path.ChangeExtension(path, ".xml");
        if (!File.Exists(xml) && docsDirectory is not null)
            xml = Path.Combine(docsDirectory, Path.GetFileNameWithoutExtension(path) + ".xml");

        var documentation = File.Exists(xml) ? XmlDocumentationProvider.CreateFromFile(xml) : null;
        return MetadataReference.CreateFromFile(path, documentation: documentation);
    }

    /// <summary>
    /// Finds <c>packs/Microsoft.NETCore.App.Ref/&lt;version&gt;/ref/net&lt;major&gt;.0</c> under the
    /// dotnet root, preferring the version the process runs on. Returns null when there is no
    /// SDK — a bare runtime install has no targeting pack, and hover simply shows no docs then.
    /// </summary>
    private static string? FindReferencePackDocs()
    {
        try
        {
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // <root>/shared/Microsoft.NETCore.App/<version>
            var version = Path.GetFileName(runtimeDirectory);
            var root = Directory.GetParent(runtimeDirectory)?.Parent?.Parent?.FullName;
            if (root is null) return null;

            var packs = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(packs)) return null;

            var major = Environment.Version.Major;
            var candidates = Directory.EnumerateDirectories(packs)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(v => v.StartsWith(major + ".", StringComparison.Ordinal))
                .OrderByDescending(v => v == version)
                .ThenByDescending(v => Version.TryParse(v.Split('-')[0], out var parsed) ? parsed : new Version())
                .ToList();

            foreach (var candidate in candidates)
            {
                var directory = Path.Combine(packs, candidate, "ref", $"net{major}.0");
                if (Directory.Exists(directory)) return directory;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Documentation is a nicety; failing to find it must not stop the service.
        }

        return null;
    }
}
