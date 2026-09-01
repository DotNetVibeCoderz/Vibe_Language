using System.Diagnostics;
using System.Net;
using System.Text;
using CSJS.Cli;

// cs2js — the CSJS command line transpiler.
// Gravicode Studios, led by Kang Fadhil.

return Cli.Run(args);

internal static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "build" => Build(args.Skip(1).ToArray()),
                "run" => RunProject(args.Skip(1).ToArray()),
                "watch" => Watch(args.Skip(1).ToArray()),
                "serve" => Serve(args.Skip(1).ToArray()),
                "new" or "init" => NewProject(args.Skip(1).ToArray()),
                "version" or "--version" => PrintVersion(),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cs2js: {ex.Message}");
            return 1;
        }
    }

    // ---------------------------------------------------------------------------------------
    // build
    // ---------------------------------------------------------------------------------------

    private static int Build(string[] args)
    {
        var directory = ResolveDirectory(args);
        var project = CsjsProject.Load(directory);

        // `--entry` appends the call to Main(), which a host that loads the emitted module
        // itself needs — Electron loads `main` from package.json, so nothing else is there to
        // start the program.
        var callEntryPoint = args.Contains("--entry");

        Console.WriteLine($"Building {project.Name}...");
        var report = new BuildRunner(project, directory, callEntryPoint).Run();

        if (!report.Success)
        {
            Console.Error.WriteLine($"Build failed with {report.ErrorCount} error(s).");
            return 1;
        }

        var warnings = report.WarningCount > 0 ? $", {report.WarningCount} warning(s)" : "";
        Console.WriteLine($"Built {report.ModuleCount} module(s) in {report.Duration.TotalMilliseconds:F0} ms{warnings}.");
        return 0;
    }

    // ---------------------------------------------------------------------------------------
    // run
    // ---------------------------------------------------------------------------------------

    private static int RunProject(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith('-')).ToArray();

        // `cs2js run file.csjs` transpiles one file and executes it, which is the fastest loop
        // for trying something out.
        if (positional.Length > 0 && File.Exists(positional[0]))
            return RunSingleFile(positional[0]);

        var directory = ResolveDirectory(args);
        var project = CsjsProject.Load(directory);
        var report = new BuildRunner(project, directory, emitEntryPointCall: true).Run(quiet: true);
        if (!report.Success) return 1;

        var outputRoot = project.ResolveOutputRoot(directory);
        var entry = FindEntryPoint(outputRoot);
        if (entry is null)
        {
            Console.Error.WriteLine($"No entry module found in {outputRoot}. Expected Program.js or Main.js.");
            return 1;
        }

        return RunNode(entry);
    }

    private static int RunSingleFile(string path)
    {
        var full = Path.GetFullPath(path);
        var work = Path.Combine(Path.GetTempPath(), "csjs-run", Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(work);

        try
        {
            var options = new CSJS.Core.TranspileOptions
            {
                EmitSourceMap = false,
                EmitTypeDefinitions = false,
                // The module is the whole program here, so it has to invoke its own Main.
                EmitEntryPointCall = true,
            };
            var result = new CSJS.Core.Transpiler(options)
                .TranspileSource(File.ReadAllText(full), Path.GetFileName(full));

            foreach (var diagnostic in result.Diagnostics)
                (diagnostic.Severity == CSJS.Core.DiagnosticSeverity.Error ? Console.Error : Console.Out)
                    .WriteLine("  " + diagnostic);

            if (!result.Success) return 1;

            foreach (var module in result.Modules)
                File.WriteAllText(Path.Combine(work, module.FileName), module.JavaScript);

            if (result.RuntimeJavaScript is not null)
                File.WriteAllText(Path.Combine(work, "csjs-runtime.js"), result.RuntimeJavaScript);

            // package.json with type=module lets Node treat the .js output as ESM.
            File.WriteAllText(Path.Combine(work, "package.json"), "{\"type\":\"module\"}");

            var entry = Path.Combine(work, result.Modules[0].FileName);
            return RunNode(entry);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string? FindEntryPoint(string outputRoot)
    {
        foreach (var candidate in new[] { "Program.js", "Main.js", "main.js", "index.js" })
        {
            var path = Path.Combine(outputRoot, candidate);
            if (File.Exists(path)) return path;
        }
        return Directory.Exists(outputRoot)
            ? Directory.EnumerateFiles(outputRoot, "*.js").FirstOrDefault()
            : null;
    }

    private static int RunNode(string entryPath)
    {
        var directory = Path.GetDirectoryName(entryPath)!;
        var packageJson = Path.Combine(directory, "package.json");
        if (!File.Exists(packageJson))
            File.WriteAllText(packageJson, "{\"type\":\"module\"}");

        var startInfo = new ProcessStartInfo("node", $"\"{entryPath}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = directory,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Console.Error.WriteLine("Could not start node.");
                return 1;
            }
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine("node was not found on PATH. Install Node.js to use `cs2js run`.");
            return 1;
        }
    }

    // ---------------------------------------------------------------------------------------
    // watch
    // ---------------------------------------------------------------------------------------

    private static int Watch(string[] args)
    {
        var directory = ResolveDirectory(args);
        var project = CsjsProject.Load(directory);
        var sourceRoot = project.ResolveSourceRoot(directory);
        var runner = new BuildRunner(project, directory);

        Console.WriteLine($"Watching {sourceRoot} — press Ctrl+C to stop.");
        RebuildAndReport(runner);

        using var watcher = new FileSystemWatcher(sourceRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        // Editors write files in several steps, so a short debounce avoids rebuilding
        // against a half-written file.
        var pending = 0;
        var gate = new object();

        void Schedule(object? sender, FileSystemEventArgs e)
        {
            var extension = Path.GetExtension(e.FullPath);
            if (extension is not (".cs" or ".csjs" or ".json")) return;

            lock (gate)
            {
                pending++;
                var mine = pending;
                Task.Delay(150).ContinueWith(_ =>
                {
                    lock (gate)
                    {
                        if (mine != pending) return;
                        Console.WriteLine();
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {Path.GetFileName(e.FullPath)} changed");
                        RebuildAndReport(runner);
                    }
                });
            }
        }

        watcher.Changed += Schedule;
        watcher.Created += Schedule;
        watcher.Renamed += Schedule;

        var exit = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            exit.Set();
        };
        exit.Wait();
        return 0;
    }

    private static void RebuildAndReport(BuildRunner runner)
    {
        var report = runner.Run(quiet: true);
        if (report.Success)
            Console.WriteLine($"  OK — {report.ModuleCount} module(s), {report.Duration.TotalMilliseconds:F0} ms");
        else
            Console.WriteLine($"  {report.ErrorCount} error(s)");
    }

    // ---------------------------------------------------------------------------------------
    // serve
    // ---------------------------------------------------------------------------------------

    private static int Serve(string[] args)
    {
        var directory = ResolveDirectory(args);
        var project = CsjsProject.Load(directory);
        var port = ReadOption(args, "--port") is { } p && int.TryParse(p, out var parsed) ? parsed : 5080;

        var report = new BuildRunner(project, directory).Run(quiet: true);
        if (!report.Success) return 1;

        var outputRoot = project.ResolveOutputRoot(directory);
        // The configured path is relative to the project, not to wherever cs2js was invoked from.
        // Resolving it against the current directory made `cs2js serve <dir>` serve a 404 for the
        // host page from anywhere but inside the project itself.
        var htmlSource = project.Html is { Length: > 0 } configured
            ? Path.GetFullPath(Path.Combine(directory, configured))
            : FindHtml(directory);
        if (htmlSource is not null && File.Exists(htmlSource))
            File.Copy(htmlSource, Path.Combine(outputRoot, "index.html"), overwrite: true);

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

        Console.WriteLine($"Serving {outputRoot}");
        Console.WriteLine($"  {prefix} — press Ctrl+C to stop");

        var running = true;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            running = false;
            listener.Stop();
        };

        while (running)
        {
            HttpListenerContext context;
            try { context = listener.GetContext(); }
            catch (HttpListenerException) { break; }

            ServeRequest(context, outputRoot);
        }

        return 0;
    }

    private static void ServeRequest(HttpListenerContext context, string root)
    {
        var relative = context.Request.Url?.AbsolutePath.TrimStart('/') ?? "";
        if (relative.Length == 0) relative = "index.html";

        var path = Path.GetFullPath(Path.Combine(root, relative));

        // Refuse anything that escapes the output directory.
        if (!path.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        context.Response.ContentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" or ".map" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".cs" or ".csjs" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };

        var bytes = File.ReadAllBytes(path);
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
    }

    private static string? FindHtml(string directory)
    {
        foreach (var candidate in new[] { "index.html", "wwwroot/index.html", "public/index.html" })
        {
            var path = Path.Combine(directory, candidate);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------
    // new
    // ---------------------------------------------------------------------------------------

    private static int NewProject(string[] args)
    {
        var name = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "MyCsjsApp";
        var directory = Path.GetFullPath(name);

        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Console.Error.WriteLine($"{directory} already exists and is not empty.");
            return 1;
        }

        Directory.CreateDirectory(Path.Combine(directory, "src"));

        new CsjsProject { Name = name, Html = "index.html" }.Save(directory);

        File.WriteAllText(Path.Combine(directory, "src", "Program.csjs"), SampleProgram);
        File.WriteAllText(Path.Combine(directory, "index.html"), SampleHtml(name));
        File.WriteAllText(Path.Combine(directory, ".gitignore"), "dist/\nbin/\nobj/\nnode_modules/\n");

        Console.WriteLine($"Created {name}");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  cd {name}");
        Console.WriteLine("  cs2js serve");
        return 0;
    }

    private const string SampleProgram = """
        using System;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Entry point wired up from index.html.</summary>
        [JsExport(Global = true)]
        public static class App
        {
            public static void Start()
            {
                var heading = Document.QuerySelector("h1");
                if (heading is not null)
                {
                    heading.TextContent = "Hello from C#!";
                }

                var button = Document.GetElementById("greet");
                button?.AddEventListener("click", _ =>
                {
                    var count = 0;
                    for (var i = 1; i <= 5; i++)
                    {
                        count += i;
                    }
                    Console.WriteLine($"1 through 5 sums to {count}");
                });
            }
        }
        """;

    private static string SampleHtml(string name) => $$"""
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>{{name}}</title>
            <style>
              body { font-family: system-ui, sans-serif; margin: 4rem auto; max-width: 40rem; }
              button { font: inherit; padding: 0.5rem 1rem; border-radius: 0.5rem; cursor: pointer; }
            </style>
          </head>
          <body>
            <h1>Loading…</h1>
            <button id="greet">Count to five</button>

            <script type="module">
              import { App } from './Program.js';
              App.Start();
            </script>
          </body>
        </html>
        """;

    // ---------------------------------------------------------------------------------------
    // Shared
    // ---------------------------------------------------------------------------------------

    private static string ResolveDirectory(string[] args)
    {
        var positional = args.FirstOrDefault(a => !a.StartsWith('-'));
        return Path.GetFullPath(positional ?? Directory.GetCurrentDirectory());
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return null;
        return args[index + 1];
    }

    private static int PrintVersion()
    {
        var version = typeof(Cli).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        Console.WriteLine($"cs2js {version} — CSJS transpiler by Gravicode Studios");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            cs2js — compile C# to modern JavaScript.

            Usage:
              cs2js new <name>          Scaffold a new CSJS project
              cs2js build [dir]         Transpile the project into its output directory
              cs2js run [dir|file]      Build, then execute with Node
              cs2js watch [dir]         Rebuild whenever a source file changes
              cs2js serve [dir]         Build and serve over HTTP
              cs2js version             Print the version

            Options:
              --port <n>                Port for `serve` (default 5080)
              --entry                   Append the call to Main() when building, for a host
                                        that loads the emitted module itself (e.g. Electron)

            Project settings live in csjs.json. Every value has a default, so a project
            with sources in src/ and no config file builds without one.
            """);
    }
}
