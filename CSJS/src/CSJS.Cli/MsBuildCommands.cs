using System.Net;
using CSJS.Core;

namespace CSJS.Cli;

/// <summary>
/// The commands the <c>CSJS.Build</c> MSBuild package drives.
/// <para>
/// A <c>.csproj</c> already knows its sources, defines, and output location, so <c>compile</c>
/// takes them on the command line instead of reading <c>csjs.json</c>. Diagnostics are written in
/// MSBuild's canonical <c>file(line,col): error ID: message</c> form with absolute paths, which is
/// what puts CSJS errors into the Visual Studio and Rider error lists next to the compiler's own.
/// </para>
/// </summary>
internal static class MsBuildCommands
{
    /// <summary>
    /// <c>cs2js compile --project-dir D --out O --sources S.rsp [--support X.rsp] [--source-root R] [--name N]
    /// [--define A;B] [--no-dts] [--no-maps] [--check-overflow] [--entry] [--module script]</c>
    /// </summary>
    public static int Compile(string[] args)
    {
        var projectDir = Path.GetFullPath(Option(args, "--project-dir") ?? Directory.GetCurrentDirectory());
        var outputDir = Path.GetFullPath(Path.Combine(projectDir, Option(args, "--out") ?? "dist"));

        // Output mirrors the folders under the source root, so `src/App.cs` with a source root of
        // `src` becomes `App.js` — the layout csjs.json's "source" setting gives the CLI build.
        var sourceRoot = Path.GetFullPath(Path.Combine(projectDir, Option(args, "--source-root") ?? "."));
        var sourceList = Option(args, "--sources") ?? throw new ArgumentException("--sources <file> is required.");

        // Support files are compiled but not emitted: the SDK's generated GlobalUsings.g.cs is
        // what the user's code relies on for implicit usings, while AssemblyInfo.cs and its kin
        // have no JavaScript to become.
        var sources = ReadList(sourceList);
        var support = Option(args, "--support") is { } supportList ? ReadList(supportList) : new List<string>();

        if (sources.Count == 0)
        {
            Console.WriteLine("CSJS: no C# sources to transpile.");
            return 0;
        }

        var supportSet = new HashSet<string>(support, StringComparer.OrdinalIgnoreCase);

        // Every document path is relative to the source root, except files outside it, which
        // keep only their name; this maps either back to the file it came from.
        var byRelative = sources.Concat(support)
            .GroupBy(p => RelativeTo(sourceRoot, p), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        string FullPathOf(string relative) => byRelative.TryGetValue(relative, out var full) ? full : Path.GetFullPath(Path.Combine(sourceRoot, relative));
        var documents = sources.Concat(support)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new SourceDocument(RelativeTo(sourceRoot, path), File.ReadAllText(path)))
            .ToList();

        var options = new TranspileOptions
        {
            Module = Option(args, "--module") is "script" ? ModuleKind.Script : ModuleKind.ESM,
            EmitTypeDefinitions = !args.Contains("--no-dts"),
            EmitSourceMap = !args.Contains("--no-maps"),
            CheckOverflow = args.Contains("--check-overflow"),
            EmitEntryPointCall = args.Contains("--entry"),
            AssemblyName = Option(args, "--name") ?? new DirectoryInfo(projectDir).Name,
        };

        foreach (var symbol in (Option(args, "--define") ?? "").Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            options.DefineConstants.Add(symbol);

        options.AdditionalReferences.Add(typeof(CSJS.Testing.Assert).Assembly.Location);

        var result = new Transpiler(options).Transpile(documents);

        foreach (var diagnostic in result.Diagnostics)
        {
            var file = diagnostic.FilePath is null ? null : FullPathOf(diagnostic.FilePath);
            var located = diagnostic with { FilePath = file };
            (diagnostic.Severity == DiagnosticSeverity.Error ? Console.Error : Console.Out).WriteLine(located.ToString());
        }

        if (!result.Success) return 1;

        Directory.CreateDirectory(outputDir);
        var written = 0;

        foreach (var module in result.Modules)
        {
            if (supportSet.Contains(FullPathOf(module.SourcePath))) continue;

            var relativeDirectory = Path.GetDirectoryName(module.SourcePath) ?? "";
            var targetDirectory = Path.Combine(outputDir, relativeDirectory);
            Directory.CreateDirectory(targetDirectory);

            var jsPath = Path.Combine(targetDirectory, module.FileName);
            written += WriteIfChanged(jsPath, module.JavaScript);
            if (module.SourceMap is not null) written += WriteIfChanged(jsPath + ".map", module.SourceMap);
            if (module.TypeDefinitions is not null) written += WriteIfChanged(Path.ChangeExtension(jsPath, ".d.ts"), module.TypeDefinitions);
        }

        if (result.RuntimeJavaScript is not null)
            written += WriteIfChanged(Path.Combine(outputDir, "csjs-runtime.js"), result.RuntimeJavaScript);

        var emitted = result.Modules.Count(m => !supportSet.Contains(FullPathOf(m.SourcePath)));
        Console.WriteLine($"CSJS: {emitted} module(s) -> {outputDir} ({written} file(s) updated)");
        return 0;
    }

    /// <summary>
    /// <c>cs2js host [dir] [--port N]</c>: serves a folder over HTTP. ES modules do not load from
    /// <c>file://</c>, so even a page that needs no build step needs a server. <c>dotnet run</c> on a
    /// CSJS.Build project calls this.
    /// </summary>
    public static int Host(string[] args)
    {
        var root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith('-')) ?? Directory.GetCurrentDirectory());
        var port = Option(args, "--port") is { } p && int.TryParse(p, out var parsed) ? parsed : 5080;

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"{root} does not exist. Build the project first.");
            return 1;
        }

        var prefix = $"http://localhost:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.Error.WriteLine($"Could not listen on {prefix}: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Serving {root}");
        Console.WriteLine($"  {prefix} — press Ctrl+C to stop");

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            listener.Stop();
        };

        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = listener.GetContext(); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            _ = Task.Run(() => ServeFile(context, root));
        }

        return 0;
    }

    private static void ServeFile(HttpListenerContext context, string root)
    {
        try
        {
            var relative = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath.TrimStart('/') ?? "");
            if (relative.Length == 0 || relative.EndsWith('/')) relative += "index.html";

            var path = Path.GetFullPath(Path.Combine(root, relative));
            var inside = path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

            if (!inside || !File.Exists(path))
            {
                context.Response.StatusCode = 404;
                return;
            }

            context.Response.ContentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".html" or ".htm" => "text/html; charset=utf-8",
                ".js" or ".mjs" => "text/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".json" or ".map" => "application/json; charset=utf-8",
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".ico" => "image/x-icon",
                ".woff2" => "font/woff2",
                ".wasm" => "application/wasm",
                ".d.ts" or ".ts" or ".txt" => "text/plain; charset=utf-8",
                _ => "application/octet-stream",
            };
            context.Response.Headers["Cache-Control"] = "no-cache";

            var bytes = File.ReadAllBytes(path);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes);
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException)
        {
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.Close();
        }
    }

    /// <summary>
    /// Writes only when the content differs, so an unchanged module keeps its timestamp and a
    /// dev server or browser cache does not see a change that is not one.
    /// </summary>
    private static int WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content) return 0;
        File.WriteAllText(path, content);
        return 1;
    }

    private static List<string> ReadList(string path)
        => File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(Path.GetFullPath)
            .ToList();

    private static string RelativeTo(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        // A linked file outside the project keeps just its name rather than a ../ path, which
        // would otherwise place its output outside the output folder.
        return relative.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(path) : relative;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
