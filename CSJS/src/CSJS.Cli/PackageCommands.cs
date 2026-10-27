using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using CSJS.Core.Bindings;

namespace CSJS.Cli;

/// <summary>
/// <c>cs2js bind</c> and <c>cs2js add</c>: TypeScript declarations in, C# bindings out.
/// <para>
/// <c>bind</c> works on a <c>.d.ts</c> already on disk. <c>add</c> fetches a package from the npm
/// registry first — metadata, then the tarball, unpacked in memory with
/// <see cref="GZipStream"/> and <see cref="TarReader"/>, so no Node, npm, or tar has to be
/// installed — finds its typings, follows their re-exports into dependencies, and writes the
/// bindings into the project.
/// </para>
/// </summary>
internal static class PackageCommands
{
    // -------------------------------------------------------------------------------------------
    // bind
    // -------------------------------------------------------------------------------------------

    public static int Bind(string[] args)
    {
        var file = args.FirstOrDefault(a => !a.StartsWith('-') && !IsOptionValue(args, a));
        if (file is null)
        {
            Console.Error.WriteLine("Usage: cs2js bind <file.d.ts> [--module <specifier>] [--namespace <ns>] [--out <file.cs>] [--global]");
            return 1;
        }

        var path = Path.GetFullPath(file);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{path} does not exist.");
            return 1;
        }

        var options = new BindingOptions
        {
            Module = Option(args, "--module"),
            Namespace = Option(args, "--namespace"),
            Global = args.Contains("--global"),
            Resolve = new DiskResolver().Resolve,
        };

        var result = BindingGenerator.Generate(new BindingSource(path, File.ReadAllText(path)), options);

        if (Option(args, "--out") is { } output)
        {
            var target = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, result.Code);
            Console.WriteLine($"Wrote {Relative(target)}");
            PrintSummary(result);
        }
        else
        {
            Console.Write(result.Code);
            PrintSummary(result, Console.Error);
        }

        return 0;
    }

    /// <summary>Resolves imports between declaration files on disk, including <c>node_modules</c> packages.</summary>
    private sealed class DiskResolver
    {
        public BindingSource? Resolve(string from, string specifier)
        {
            var directory = Path.GetDirectoryName(from) ?? ".";

            if (specifier.StartsWith('.') || specifier.StartsWith('/'))
            {
                var found = TypingsResolution.Candidates(Path.GetFullPath(Path.Combine(directory, specifier)))
                    .FirstOrDefault(File.Exists);
                return found is null ? null : new BindingSource(found, File.ReadAllText(found));
            }

            // A bare specifier: look for the package in node_modules, walking up.
            var (name, subpath) = TypingsResolution.SplitSpecifier(specifier);
            for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
            {
                foreach (var candidate in new[] { name, TypingsResolution.TypesPackageName(name) })
                {
                    var root = Path.Combine(current, "node_modules", candidate.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(root)) continue;

                    var package = new DiskPackage(root);
                    var entry = TypingsResolution.EntryFor(package, subpath);
                    if (entry is not null)
                    {
                        var full = Path.Combine(root, entry);
                        return new BindingSource(full, File.ReadAllText(full));
                    }
                }
            }

            return null;
        }
    }

    private sealed class DiskPackage(string root) : IPackageFiles
    {
        public bool Exists(string relativePath) => File.Exists(Path.Combine(root, relativePath));
        public string? Read(string relativePath)
        {
            var path = Path.Combine(root, relativePath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }

    // -------------------------------------------------------------------------------------------
    // add
    // -------------------------------------------------------------------------------------------

    public static int Add(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith('-') && !IsOptionValue(args, a)).ToList();
        if (positional.Count == 0)
        {
            Console.Error.WriteLine("Usage: cs2js add <npm-package>[@version] [dir] [--namespace <ns>]");
            return 1;
        }

        var (packageName, requested) = SplitPackageSpec(positional[0]);
        var directory = Path.GetFullPath(positional.Count > 1 ? positional[1] : Directory.GetCurrentDirectory());
        var project = CsjsProject.Load(directory);

        using var registry = new NpmRegistry();
        Console.WriteLine($"Fetching {packageName}{(requested is null ? "" : "@" + requested)} from {NpmRegistry.BaseUrl}...");
        var package = registry.Fetch(packageName, requested ?? "latest");

        var entry = TypingsResolution.EntryFor(package, null);
        var typesPackage = package;
        if (entry is null)
        {
            // No typings of its own: DefinitelyTyped is where they live.
            var typesName = TypingsResolution.TypesPackageName(packageName);
            Console.WriteLine($"{packageName} ships no typings; trying {typesName}...");
            try
            {
                typesPackage = registry.Fetch(typesName, MajorRange(package.Version));
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                typesPackage = registry.Fetch(typesName, "latest");
            }
            entry = TypingsResolution.EntryFor(typesPackage, null);
        }

        if (entry is null)
        {
            Console.Error.WriteLine($"No TypeScript declarations found for {packageName}.");
            return 1;
        }

        Console.WriteLine($"Generating bindings from {typesPackage.Name}@{typesPackage.Version}/{entry}...");
        var resolver = new RegistryResolver(registry, typesPackage);
        var result = BindingGenerator.Generate(
            new BindingSource(RegistryResolver.VirtualPath(typesPackage, entry), typesPackage.Read(entry)!),
            new BindingOptions
            {
                Module = packageName,
                Namespace = Option(args, "--namespace"),
                Resolve = resolver.Resolve,
            });

        var sourceRoot = project.ResolveSourceRoot(directory);
        var outputPath = Path.Combine(sourceRoot, "Bindings", result.ContainerName + ".cs");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, result.Code);

        // The dependency is recorded in csjs.json, the project's own manifest — the same job
        // package.json does for npm. The exact version is pinned, because the bindings were
        // generated from exactly that version's typings.
        project.Dependencies ??= new SortedDictionary<string, string>(StringComparer.Ordinal);
        project.Dependencies[packageName] = package.Version;
        project.Save(directory);

        Console.WriteLine($"Added {packageName}@{package.Version} -> {Path.GetRelativePath(directory, outputPath)}");
        PrintSummary(result);
        Console.WriteLine($"Recorded \"{packageName}\": \"{package.Version}\" under dependencies in {CsjsProject.FileName}.");
        Console.WriteLine();
        Console.WriteLine($"The emitted JavaScript imports '{packageName}' by name. A bundler resolves that from");
        Console.WriteLine("node_modules; a page served as-is needs an import map, for example:");
        Console.WriteLine($"  <script type=\"importmap\">{{ \"imports\": {{ \"{packageName}\": \"https://esm.sh/{packageName}@{package.Version}\" }} }}</script>");
        return 0;
    }

    /// <summary>Resolves imports between declaration files inside fetched packages, fetching dependencies on demand.</summary>
    private sealed class RegistryResolver(NpmRegistry registry, NpmPackage entryPackage)
    {
        private readonly Dictionary<string, NpmPackage> _packages = new(StringComparer.Ordinal) { [entryPackage.Name] = entryPackage };

        public static string VirtualPath(NpmPackage package, string relativePath) => $"npm:{package.Name}/{relativePath}";

        public BindingSource? Resolve(string from, string specifier)
        {
            var (owner, relativeFrom) = Locate(from);
            if (owner is null) return null;

            if (specifier.StartsWith('.'))
            {
                var baseDirectory = Path.GetDirectoryName(relativeFrom)?.Replace('\\', '/') ?? "";
                var combined = Normalize(baseDirectory.Length == 0 ? specifier : baseDirectory + "/" + specifier);
                var found = TypingsResolution.Candidates(combined).FirstOrDefault(owner.Exists);
                return found is null ? null : new BindingSource(VirtualPath(owner, found), owner.Read(found)!);
            }

            var (name, subpath) = TypingsResolution.SplitSpecifier(specifier);
            var package = PackageFor(name, owner);
            if (package is null) return null;

            var entry = TypingsResolution.EntryFor(package, subpath);
            if (entry is null && subpath is null)
            {
                // A dependency without typings of its own may have them on DefinitelyTyped.
                package = PackageFor(TypingsResolution.TypesPackageName(name), owner);
                entry = package is null ? null : TypingsResolution.EntryFor(package, null);
            }

            return entry is null || package is null ? null : new BindingSource(VirtualPath(package, entry), package.Read(entry)!);
        }

        private (NpmPackage? Package, string RelativePath) Locate(string virtualPath)
        {
            if (!virtualPath.StartsWith("npm:", StringComparison.Ordinal)) return (null, "");
            var rest = virtualPath[4..];
            var (name, subpath) = TypingsResolution.SplitSpecifier(rest);
            return (_packages.GetValueOrDefault(name), subpath ?? "");
        }

        private NpmPackage? PackageFor(string name, NpmPackage requester)
        {
            if (_packages.TryGetValue(name, out var cached)) return cached;

            var range = requester.DependencyRange(name) ?? "latest";
            try
            {
                Console.WriteLine($"  fetching {name}@{range}");
                var package = registry.Fetch(name, range);
                _packages[name] = package;
                return package;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                Console.Error.WriteLine($"  could not fetch {name}: {ex.Message}");
                _packages[name] = null!;
                return null;
            }
        }

        private static string Normalize(string path)
        {
            var parts = new List<string>();
            foreach (var part in path.Replace('\\', '/').Split('/'))
            {
                if (part is "" or ".") continue;
                if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(part);
            }
            return string.Join("/", parts);
        }
    }

    // -------------------------------------------------------------------------------------------
    // Shared
    // -------------------------------------------------------------------------------------------

    private static void PrintSummary(BindingResult result, TextWriter? writer = null)
    {
        writer ??= Console.Out;
        writer.WriteLine($"  {result.TypeCount} type(s), {result.MemberCount} member(s) in namespace {result.Namespace}");
        if (result.Approximations.Count > 0)
            writer.WriteLine($"  {result.Approximations.Count} type(s) approximated — see the `// note:` comments");
        if (result.Skipped.Count > 0)
            writer.WriteLine($"  {result.Skipped.Count} construct(s) skipped — see the `// skipped:` comments");
    }

    private static (string Name, string? Version) SplitPackageSpec(string spec)
    {
        var at = spec.LastIndexOf('@');
        return at > 0 ? (spec[..at], spec[(at + 1)..]) : (spec, null);
    }

    /// <summary>The <c>@types</c> version range matching a package's major version.</summary>
    private static string MajorRange(string version) => version.Split('.')[0] is { Length: > 0 } major ? major : "latest";

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool IsOptionValue(string[] args, string value)
    {
        var index = Array.IndexOf(args, value);
        return index > 0 && args[index - 1] is "--module" or "--namespace" or "--out";
    }

    private static string Relative(string path)
    {
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }
}

/// <summary>Read access to the files of one package, wherever they came from.</summary>
internal interface IPackageFiles
{
    bool Exists(string relativePath);
    string? Read(string relativePath);
}

/// <summary>How TypeScript finds a package's declarations, reduced to what bindings need.</summary>
internal static class TypingsResolution
{
    /// <summary>Files a module specifier may refer to, in the order TypeScript tries them.</summary>
    public static IEnumerable<string> Candidates(string path)
    {
        path = path.Replace('\\', '/');
        foreach (var extension in new[] { ".d.ts", ".d.mts", ".d.cts" })
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) yield return path;

        foreach (var js in new[] { ".js", ".mjs", ".cjs" })
        {
            if (!path.EndsWith(js, StringComparison.OrdinalIgnoreCase)) continue;
            var stem = path[..^js.Length];
            yield return stem + (js == ".mjs" ? ".d.mts" : js == ".cjs" ? ".d.cts" : ".d.ts");
            yield return stem + ".d.ts";
        }

        yield return path + ".d.ts";
        yield return path + ".d.mts";
        yield return path + ".d.cts";
        yield return path + "/index.d.ts";
    }

    /// <summary><c>@scope/name/sub/path</c> into the package name and the subpath.</summary>
    public static (string Name, string? Subpath) SplitSpecifier(string specifier)
    {
        var parts = specifier.Split('/');
        var nameParts = specifier.StartsWith('@') && parts.Length > 1 ? 2 : 1;
        var name = string.Join("/", parts.Take(nameParts));
        var subpath = parts.Length > nameParts ? string.Join("/", parts.Skip(nameParts)) : null;
        return (name, subpath);
    }

    /// <summary>The DefinitelyTyped package name: <c>three</c> → <c>@types/three</c>, <c>@a/b</c> → <c>@types/a__b</c>.</summary>
    public static string TypesPackageName(string name)
        => name.StartsWith('@') ? "@types/" + name[1..].Replace("/", "__") : "@types/" + name;

    /// <summary>The declaration file a package (or one of its subpaths) resolves to, relative to its root.</summary>
    public static string? EntryFor(IPackageFiles package, string? subpath)
    {
        JsonElement manifest = default;
        var hasManifest = false;
        if (package.Read("package.json") is { } json)
        {
            try
            {
                manifest = JsonDocument.Parse(json).RootElement;
                hasManifest = true;
            }
            catch (JsonException) { }
        }

        if (hasManifest && manifest.TryGetProperty("exports", out var exports))
        {
            var key = subpath is null ? "." : "./" + subpath;
            var target = exports.ValueKind == JsonValueKind.Object && exports.TryGetProperty(key, out var entry)
                ? entry
                : subpath is null && exports.ValueKind != JsonValueKind.Object ? exports
                : subpath is null && exports.ValueKind == JsonValueKind.Object && !exports.EnumerateObject().Any(p => p.Name.StartsWith('.')) ? exports
                : default;
            if (TypesCondition(target) is { } fromExports && Existing(package, fromExports) is { } resolved) return resolved;
        }

        if (subpath is not null)
            return Candidates(subpath).FirstOrDefault(package.Exists);

        if (hasManifest)
        {
            foreach (var field in new[] { "types", "typings" })
            {
                if (manifest.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
                    && Existing(package, value.GetString()!) is { } resolved)
                {
                    return resolved;
                }
            }
        }

        if (package.Exists("index.d.ts")) return "index.d.ts";

        if (hasManifest && manifest.TryGetProperty("main", out var main) && main.ValueKind == JsonValueKind.String)
            return Candidates(Trim(main.GetString()!)).FirstOrDefault(package.Exists);

        return null;
    }

    /// <summary>The <c>types</c> target of a conditional export, searching nested conditions.</summary>
    private static string? TypesCondition(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString()!;
                return value.EndsWith(".d.ts", StringComparison.Ordinal) || value.EndsWith(".d.mts", StringComparison.Ordinal) ? value : null;

            case JsonValueKind.Object:
                foreach (var condition in new[] { "types", "import", "default", "require", "node" })
                {
                    if (element.TryGetProperty(condition, out var nested) && TypesCondition(nested) is { } found)
                        return found;
                }
                return null;

            case JsonValueKind.Array:
                return element.EnumerateArray().Select(TypesCondition).FirstOrDefault(t => t is not null);
        }
        return null;
    }

    private static string? Existing(IPackageFiles package, string path)
        => Candidates(Trim(path)).FirstOrDefault(package.Exists);

    private static string Trim(string path) => path.Replace('\\', '/').TrimStart('.', '/');
}

/// <summary>One package version, unpacked in memory: its declaration files and manifest.</summary>
internal sealed class NpmPackage : IPackageFiles
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required Dictionary<string, string> Files { get; init; }
    public JsonElement Metadata { get; init; }

    public bool Exists(string relativePath) => Files.ContainsKey(relativePath.Replace('\\', '/'));
    public string? Read(string relativePath) => Files.GetValueOrDefault(relativePath.Replace('\\', '/'));

    /// <summary>The version range this package declares for a dependency, if it declares one.</summary>
    public string? DependencyRange(string dependency)
    {
        foreach (var field in new[] { "dependencies", "peerDependencies", "optionalDependencies", "devDependencies" })
        {
            if (Metadata.ValueKind == JsonValueKind.Object
                && Metadata.TryGetProperty(field, out var dependencies)
                && dependencies.TryGetProperty(dependency, out var range)
                && range.ValueKind == JsonValueKind.String)
            {
                var text = range.GetString()!;
                // `workspace:` and `npm:` aliases are publishing artifacts, not ranges.
                return text.Contains(':') ? "latest" : text;
            }
        }
        return null;
    }
}

/// <summary>The npm registry: metadata over HTTPS, tarballs unpacked in memory.</summary>
internal sealed class NpmRegistry : IDisposable
{
    public const string BaseUrl = "https://registry.npmjs.org";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public NpmRegistry() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("cs2js (CSJS; +https://github.com/DotNetVibeCoderz/Vibe_Language)");

    public NpmPackage Fetch(string name, string range)
    {
        var encoded = name.StartsWith('@') ? "@" + Uri.EscapeDataString(name[1..]) : Uri.EscapeDataString(name);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{encoded}");
        // The abbreviated document is a fraction of the full one and has everything needed here.
        request.Headers.Accept.ParseAdd("application/vnd.npm.install-v1+json");

        using var response = _http.Send(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{name}: the registry answered {(int)response.StatusCode} {response.ReasonPhrase}");

        using var document = JsonDocument.Parse(response.Content.ReadAsStream());
        var root = document.RootElement;
        var version = SemverRange.Resolve(root, range)
                      ?? throw new InvalidOperationException($"{name}: no published version satisfies '{range}'");
        var metadata = root.GetProperty("versions").GetProperty(version).Clone();
        var tarball = metadata.GetProperty("dist").GetProperty("tarball").GetString()!;

        return new NpmPackage { Name = name, Version = version, Files = DownloadTypings(tarball), Metadata = metadata };
    }

    /// <summary>Downloads a tarball and keeps only what bindings need: declarations and manifests.</summary>
    private Dictionary<string, string> DownloadTypings(string url)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = _http.GetStreamAsync(url).GetAwaiter().GetResult();
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null) continue;

            // Every entry sits under one top-level folder — usually `package/`, but not always.
            var name = entry.Name.Replace('\\', '/');
            var slash = name.IndexOf('/');
            var relative = slash >= 0 ? name[(slash + 1)..] : name;

            var keep = relative.EndsWith(".d.ts", StringComparison.Ordinal)
                       || relative.EndsWith(".d.mts", StringComparison.Ordinal)
                       || relative.EndsWith(".d.cts", StringComparison.Ordinal)
                       || relative.EndsWith("package.json", StringComparison.Ordinal);
            if (!keep) continue;

            using var reader = new StreamReader(entry.DataStream);
            files[relative] = reader.ReadToEnd();
        }

        return files;
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Just enough of npm's semver ranges to pick a version: exact versions, dist-tags, <c>^</c>,
/// <c>~</c>, comparison operators, x-ranges, hyphen ranges, and <c>||</c>.
/// </summary>
internal static class SemverRange
{
    public static string? Resolve(JsonElement metadata, string range)
    {
        range = range.Trim();
        if (metadata.TryGetProperty("dist-tags", out var tags) && tags.TryGetProperty(range.Length == 0 ? "latest" : range, out var tagged))
            return tagged.GetString();

        var versions = metadata.GetProperty("versions").EnumerateObject().Select(p => p.Name).ToList();
        if (versions.Contains(range)) return range;

        var allowPrerelease = range.Contains('-');
        return versions
            .Select(v => (Text: v, Parsed: Parse(v)))
            .Where(v => v.Parsed is not null && (allowPrerelease || v.Parsed.Value.Pre.Length == 0))
            .Where(v => Satisfies(v.Parsed!.Value, range))
            .OrderByDescending(v => v.Parsed!.Value, Comparer<Version3>.Create(Compare))
            .Select(v => v.Text)
            .FirstOrDefault();
    }

    private readonly record struct Version3(int Major, int Minor, int Patch, string Pre);

    private static Version3? Parse(string text)
    {
        text = text.Trim().TrimStart('v', '=');
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        var dash = text.IndexOf('-');
        var pre = dash >= 0 ? text[(dash + 1)..] : "";
        var core = (dash >= 0 ? text[..dash] : text).Split('.');
        int Part(int i) => i < core.Length && int.TryParse(core[i], out var n) ? n : 0;
        if (core.Length == 0 || !int.TryParse(core[0], out _)) return null;
        return new Version3(Part(0), Part(1), Part(2), pre);
    }

    private static int Compare(Version3 a, Version3 b)
    {
        var c = a.Major.CompareTo(b.Major);
        if (c == 0) c = a.Minor.CompareTo(b.Minor);
        if (c == 0) c = a.Patch.CompareTo(b.Patch);
        if (c != 0) return c;
        if (a.Pre.Length == 0) return b.Pre.Length == 0 ? 0 : 1;
        return b.Pre.Length == 0 ? -1 : string.CompareOrdinal(a.Pre, b.Pre);
    }

    private static bool Satisfies(Version3 version, string range)
    {
        foreach (var alternative in range.Split("||"))
        {
            var set = alternative.Trim();
            if (set.Length == 0 || set is "*" or "x" or "latest") return true;

            // Hyphen range: `1.2.3 - 2.3.4`.
            var hyphen = set.Split(" - ");
            if (hyphen.Length == 2)
            {
                if (Compare(version, Low(hyphen[0])) >= 0 && WithinUpper(version, hyphen[1])) return true;
                continue;
            }

            var comparators = set.Replace(">= ", ">=").Replace("<= ", "<=").Replace("> ", ">").Replace("< ", "<")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (comparators.All(c => Matches(version, c))) return true;
        }
        return false;
    }

    private static Version3 Low(string partial) => Parse(partial.Replace("x", "0").Replace("*", "0")) ?? new Version3(0, 0, 0, "");

    private static bool WithinUpper(Version3 version, string upper)
    {
        var parts = upper.Trim().Split('.');
        var parsed = Low(upper);
        return parts.Length switch
        {
            1 => version.Major <= parsed.Major,
            2 => version.Major < parsed.Major || version.Major == parsed.Major && version.Minor <= parsed.Minor,
            _ => Compare(version, parsed) <= 0,
        };
    }

    private static bool Matches(Version3 version, string comparator)
    {
        string op;
        if (comparator.StartsWith(">=") || comparator.StartsWith("<=")) op = comparator[..2];
        else if (comparator[0] is '>' or '<' or '^' or '~' or '=') op = comparator[..1];
        else op = "";
        var operand = comparator[op.Length..].TrimStart('v');
        if (op == "~" && operand.StartsWith('>')) operand = operand[1..];

        var parts = operand.Split('-')[0].Split('.');
        var specified = parts.TakeWhile(p => p.Length > 0 && p != "x" && p != "X" && p != "*").Count();
        var target = Low(operand);

        switch (op)
        {
            case ">=": return Compare(version, target) >= 0;
            case ">": return Compare(version, target) > 0;
            case "<=": return Compare(version, target) <= 0;
            case "<": return Compare(version, target) < 0;

            case "^":
                if (Compare(version, target) < 0) return false;
                if (target.Major > 0 || specified == 1) return version.Major == target.Major;
                if (target.Minor > 0 || specified == 2) return version.Major == 0 && version.Minor == target.Minor;
                return version.Major == 0 && version.Minor == 0 && version.Patch == target.Patch;

            case "~":
                if (Compare(version, target) < 0) return false;
                return specified == 1 ? version.Major == target.Major : version.Major == target.Major && version.Minor == target.Minor;

            default:
                // Exact or x-range: every specified part must match.
                if (specified >= 1 && version.Major != target.Major) return false;
                if (specified >= 2 && version.Minor != target.Minor) return false;
                if (specified >= 3 && (version.Patch != target.Patch || version.Pre != target.Pre)) return false;
                return true;
        }
    }
}
