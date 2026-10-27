namespace CSJS.Playground;

/// <summary>
/// Downloads the reference assemblies the transpiler binds against.
/// <para>
/// WebAssembly has no filesystem, so Roslyn cannot load references by path the way it does on
/// the desktop. The app's own assemblies are already served from <c>_framework/</c>, so they
/// are fetched over HTTP once and reused for every transpile.
/// </para>
/// </summary>
public sealed class ReferenceLoader
{
    private readonly HttpClient _http;
    private List<byte[]>? _cached;

    public ReferenceLoader(HttpClient http) => _http = http;

    /// <summary>
    /// The assemblies user code can reference. This is the BCL surface the transpiler maps,
    /// plus the interop library that carries the attributes and browser bindings, and CSJS.Web —
    /// the whole web platform — so anything written against <c>CSJS.Web</c> transpiles here too.
    /// </summary>
    private static readonly string[] AssemblyNames =
    {
        "System.Runtime",
        "System.Private.CoreLib",
        "System.Console",
        "System.Linq",
        "System.Linq.Expressions",
        "System.Collections",
        "System.Runtime.Extensions",
        "System.Text.RegularExpressions",
        "System.Threading",
        "System.Threading.Tasks",
        "System.Memory",
        "System.Text.Json",
        "netstandard",
        "CSJS.Interop",
        "CSJS.Web",
    };

    /// <summary>Fetches the reference set, downloading it on first use.</summary>
    public async Task<IReadOnlyList<byte[]>> GetAsync()
    {
        if (_cached is not null) return _cached;

        var images = new List<byte[]>();

        foreach (var name in AssemblyNames)
        {
            var bytes = await TryDownloadAsync(name);
            if (bytes is not null) images.Add(bytes);
        }

        _cached = images;
        return images;
    }

    /// <summary>
    /// Downloads one assembly. Blazor fingerprints assembly file names, so the resource may sit
    /// under a hashed name; the boot manifest is the only reliable way to find it.
    /// </summary>
    private async Task<byte[]?> TryDownloadAsync(string name)
    {
        foreach (var candidate in await CandidatePathsAsync(name))
        {
            try
            {
                using var response = await _http.GetAsync(candidate);
                if (!response.IsSuccessStatusCode) continue;

                var bytes = await response.Content.ReadAsByteArrayAsync();

                // A missing file can come back as the SPA fallback page rather than a 404.
                if (bytes.Length > 2 && bytes[0] == 'M' && bytes[1] == 'Z') return bytes;
            }
            catch (HttpRequestException)
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    private Dictionary<string, string>? _manifest;

    private async Task<IReadOnlyList<string>> CandidatePathsAsync(string name)
    {
        _manifest ??= await LoadManifestAsync();

        var paths = new List<string>();
        if (_manifest.TryGetValue(name, out var fromManifest)) paths.Add(fromManifest);

        // Unfingerprinted layouts still serve the plain name.
        paths.Add($"_framework/{name}.wasm");
        paths.Add($"_framework/{name}.dll");

        return paths;
    }

    /// <summary>
    /// Reads the Blazor boot manifest to map assembly names onto the files actually served.
    /// </summary>
    private async Task<Dictionary<string, string>> LoadManifestAsync()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var response = await _http.GetAsync("_framework/blazor.boot.json");
            if (!response.IsSuccessStatusCode) return map;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await System.Text.Json.JsonDocument.ParseAsync(stream);

            foreach (var file in EnumerateAssemblyEntries(document.RootElement))
            {
                // Entries look like "System.Runtime.abc123.wasm"; the assembly name is the part
                // before the fingerprint, which is itself before the extension.
                var withoutExtension = Path.GetFileNameWithoutExtension(file);
                var assemblyName = Path.GetFileNameWithoutExtension(withoutExtension);

                map[assemblyName] = $"_framework/{file}";
                map[withoutExtension] = $"_framework/{file}";
            }
        }
        catch (Exception)
        {
            // Without the manifest the plain-name candidates still stand a chance.
        }

        return map;
    }

    private static IEnumerable<string> EnumerateAssemblyEntries(System.Text.Json.JsonElement root)
    {
        // The manifest shape has changed across releases, so several locations are checked.
        foreach (var path in new[] { "resources", "assets" })
        {
            if (!root.TryGetProperty(path, out var container)) continue;

            foreach (var group in new[] { "assembly", "coreAssembly", "fingerprinting" })
            {
                if (!container.TryGetProperty(group, out var entries)) continue;

                if (entries.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var entry in entries.EnumerateObject())
                    {
                        // fingerprinting maps served-name -> original-name; the key is the file.
                        yield return group == "fingerprinting" ? entry.Name : entry.Name;
                    }
                }
            }
        }
    }
}
