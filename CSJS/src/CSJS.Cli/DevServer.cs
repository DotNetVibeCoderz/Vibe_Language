using System.Net;
using System.Text;
using System.Text.Json;
using CSJS.Core;

namespace CSJS.Cli;

/// <summary>
/// <c>cs2js serve</c>: serves the build output and, unless <c>--no-reload</c> is given, keeps it
/// live — a save rebuilds and the open page follows.
/// <para>
/// What "follows" means is deliberately modest. A stylesheet change is swapped into the page in
/// place, without a reload, because CSS has no state to lose. A code change reloads the page:
/// JavaScript modules cannot be replaced in a running page without the code cooperating, and a
/// reload is the honest version of that. A build that fails does not leave the old page running
/// as if nothing happened — the errors are drawn over it until the next good build.
/// </para>
/// <para>
/// The page learns about builds through Server-Sent Events from a small script injected into
/// every HTML response. Each request is handled on the thread pool, so an open event stream never
/// blocks the files the page is asking for.
/// </para>
/// </summary>
internal sealed class DevServer
{
    private const string EventsPath = "/__csjs/events";
    private const string ClientPath = "/__csjs/client.js";

    private readonly string _directory;
    private readonly int _port;
    private readonly bool _reload;
    private readonly BuildSettings _settings;
    private readonly object _buildGate = new();
    private readonly List<HttpListenerResponse> _clients = new();

    private CsjsProject _project;
    private BuildRunner _runner;
    private string _outputRoot;

    /// <summary>The event a newly connected page should see first: the last build's outcome.</summary>
    private string? _lastFailure;

    public DevServer(CsjsProject project, string directory, int port, bool reload, BuildSettings settings)
    {
        _project = project;
        _directory = directory;
        _port = port;
        _reload = reload;
        _settings = settings;
        _runner = new BuildRunner(project, directory, settings: settings);
        _outputRoot = project.ResolveOutputRoot(directory);
    }

    public int Run()
    {
        var report = _runner.Run(quiet: true);

        // Without live reload this is the old `serve`: a failed build is a failed command. With it,
        // the server starts anyway and shows the errors in the page, since fixing them is the next
        // thing the developer will do.
        if (!report.Success)
        {
            if (!_reload) return 1;
            _lastFailure = FailureEvent(report);
        }

        CopyHostPage();

        var prefix = $"http://localhost:{_port}/";
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

        Console.WriteLine($"Serving {_outputRoot}");
        Console.WriteLine($"  {prefix} — {(_reload ? "live reload on, " : "")}press Ctrl+C to stop");

        using var watcher = _reload ? StartWatching() : null;

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
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { break; }

            ThreadPool.QueueUserWorkItem(_ => Handle(context));
        }

        lock (_clients)
        {
            foreach (var client in _clients) TryClose(client);
            _clients.Clear();
        }

        return 0;
    }

    // ---------------------------------------------------------------------------------------
    // Requests
    // ---------------------------------------------------------------------------------------

    private void Handle(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";

            if (_reload && path == EventsPath)
            {
                OpenEventStream(context.Response);
                return;
            }

            if (_reload && path == ClientPath)
            {
                Send(context.Response, "text/javascript; charset=utf-8", Encoding.UTF8.GetBytes(ClientScript));
                return;
            }

            ServeFile(context, path);
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
        {
            // The browser went away mid-response; nothing to do.
        }
    }

    private void ServeFile(HttpListenerContext context, string urlPath)
    {
        var relative = Uri.UnescapeDataString(urlPath.TrimStart('/'));
        if (relative.Length == 0 || relative.EndsWith('/')) relative += "index.html";

        var root = Path.GetFullPath(_outputRoot);
        var path = Path.GetFullPath(Path.Combine(root, relative));

        // Refuse anything that escapes the output directory.
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var bytes = File.ReadAllBytes(path);

        if (_reload && extension is ".html" or ".htm")
            bytes = Encoding.UTF8.GetBytes(InjectClient(Encoding.UTF8.GetString(bytes)));

        Send(context.Response, ContentType(extension), bytes);
    }

    private static void Send(HttpListenerResponse response, string contentType, byte[] bytes)
    {
        response.ContentType = contentType;
        // A reload must fetch what the rebuild just wrote, never a cached module.
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
        response.Close();
    }

    internal static string ContentType(string extension) => extension switch
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
        ".wasm" => "application/wasm",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".txt" or ".cs" or ".csjs" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    /// <summary>Adds the live-reload client to a page, in its head when it has one.</summary>
    internal static string InjectClient(string html)
    {
        const string tag = "<script type=\"module\" src=\"" + ClientPath + "\"></script>";
        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (head >= 0) return html.Insert(head, tag);
        var body = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return body >= 0 ? html.Insert(body, tag) : html + tag;
    }

    // ---------------------------------------------------------------------------------------
    // Server-Sent Events
    // ---------------------------------------------------------------------------------------

    private void OpenEventStream(HttpListenerResponse response)
    {
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.SendChunked = true;

        // The stream stays open; only the first write happens here. `retry` makes a page whose
        // server restarted reconnect quickly.
        var greeting = "retry: 1000\n\n" + (_lastFailure ?? "");
        if (!TryWrite(response, greeting)) return;

        lock (_clients) _clients.Add(response);
    }

    private void Broadcast(string eventText)
    {
        lock (_clients)
        {
            foreach (var client in _clients.ToList())
            {
                if (TryWrite(client, eventText)) continue;
                _clients.Remove(client);
                TryClose(client);
            }
        }
    }

    private static bool TryWrite(HttpListenerResponse response, string text)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            response.OutputStream.Write(bytes);
            response.OutputStream.Flush();
            return true;
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryClose(HttpListenerResponse response)
    {
        try { response.Close(); } catch (Exception) { /* already gone */ }
    }

    internal static string Event(string name, object data)
        => $"event: {name}\ndata: {JsonSerializer.Serialize(data)}\n\n";

    private static string FailureEvent(BuildReport report) => Event("build-error", new
    {
        errors = report.ErrorCount,
        diagnostics = report.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .DefaultIfEmpty("The build failed; see the terminal for details.")
            .ToList(),
    });

    // ---------------------------------------------------------------------------------------
    // Watching
    // ---------------------------------------------------------------------------------------

    private FileSystemWatcher StartWatching()
    {
        var watcher = new FileSystemWatcher(_directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };

        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generation = 0;

        void Schedule(string fullPath)
        {
            if (Ignored(fullPath)) return;

            lock (pending)
            {
                pending.Add(fullPath);
                var mine = ++generation;

                // Editors save in several steps; a short debounce builds once, against the
                // finished file.
                Task.Delay(150).ContinueWith(_ =>
                {
                    string[] changed;
                    lock (pending)
                    {
                        if (mine != generation) return;
                        changed = pending.ToArray();
                        pending.Clear();
                    }
                    Rebuild(changed);
                });
            }
        }

        watcher.Changed += (_, e) => Schedule(e.FullPath);
        watcher.Created += (_, e) => Schedule(e.FullPath);
        watcher.Deleted += (_, e) => Schedule(e.FullPath);
        watcher.Renamed += (_, e) => Schedule(e.FullPath);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    /// <summary>Paths whose changes never matter: the output itself, build caches, tooling folders.</summary>
    private bool Ignored(string fullPath)
    {
        var full = Path.GetFullPath(fullPath);
        if (full.StartsWith(Path.GetFullPath(_outputRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        var relative = Path.GetRelativePath(_directory, full);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => s is "obj" or "bin" or "node_modules" or ".git" or ".vs" or ".idea" or ".vscode");
    }

    private void Rebuild(string[] changed)
    {
        lock (_buildGate)
        {
            var names = string.Join(", ", changed.Select(Path.GetFileName).Distinct().Take(4));
            Console.WriteLine();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {names} changed");

            // A changed csjs.json can move the output or change every option; start over from it.
            if (changed.Any(c => Path.GetFileName(c).Equals(CsjsProject.FileName, StringComparison.OrdinalIgnoreCase)))
            {
                _project = CsjsProject.Load(_directory);
                _runner = new BuildRunner(_project, _directory, settings: _settings);
                _outputRoot = _project.ResolveOutputRoot(_directory);
            }

            BuildReport report;
            try
            {
                report = _runner.Run(quiet: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  build crashed: {ex.Message}");
                _lastFailure = Event("build-error", new { errors = 1, diagnostics = new[] { ex.Message } });
                Broadcast(_lastFailure);
                return;
            }

            if (!report.Success)
            {
                Console.WriteLine($"  {report.ErrorCount} error(s) — shown in the page");
                _lastFailure = FailureEvent(report);
                Broadcast(_lastFailure);
                return;
            }

            _lastFailure = null;
            CopyHostPage();

            var stats = report.Incremental is { } s ? $", {s.Emitted} emitted / {s.Reused} reused" : "";
            Console.WriteLine($"  OK — {report.ModuleCount} module(s){stats}, {report.Duration.TotalMilliseconds:F0} ms");

            // Only stylesheets changed: swap them in place. Anything else reloads the page.
            var onlyCss = changed.All(c => Path.GetExtension(c).Equals(".css", StringComparison.OrdinalIgnoreCase));
            if (onlyCss)
            {
                var sourceRoot = _project.ResolveSourceRoot(_directory);
                var files = changed.Select(c =>
                {
                    var full = Path.GetFullPath(c);
                    var root = full.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        ? sourceRoot
                        : Path.GetFullPath(_directory);
                    return Path.GetRelativePath(root, full).Replace('\\', '/');
                }).ToList();
                Broadcast(Event("css", files));
            }
            else
            {
                Broadcast(Event("reload", new { }));
            }
        }
    }

    /// <summary>
    /// Copies the configured host page in as <c>index.html</c>. Resolved against the project, not
    /// the current directory, so serving a project from elsewhere works.
    /// </summary>
    private void CopyHostPage()
    {
        var htmlSource = _project.Html is { Length: > 0 } configured
            ? Path.GetFullPath(Path.Combine(_directory, configured))
            : FindHtml(_directory);

        if (htmlSource is null || !File.Exists(htmlSource)) return;

        Directory.CreateDirectory(_outputRoot);
        File.Copy(htmlSource, Path.Combine(_outputRoot, "index.html"), overwrite: true);
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
    // The page side
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Injected into every served page. Listens for build events: reloads on code changes, swaps
    /// stylesheets on CSS changes, and draws transpile errors over the page when a build fails.
    /// </summary>
    internal const string ClientScript = """
        // CSJS live reload. Injected by `cs2js serve`; not part of your build output.
        const OVERLAY_ID = 'csjs-error-overlay';
        const source = new EventSource('/__csjs/events');

        function clearOverlay() {
          document.getElementById(OVERLAY_ID)?.remove();
        }

        function showOverlay(report) {
          clearOverlay();
          const overlay = document.createElement('div');
          overlay.id = OVERLAY_ID;
          overlay.style.cssText = [
            'position:fixed', 'inset:0', 'z-index:2147483647', 'overflow:auto',
            'background:rgba(16,12,24,0.94)', 'color:#f4f1fa', 'padding:2rem',
            'font:13px/1.55 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace',
          ].join(';');

          const title = document.createElement('div');
          title.textContent = `cs2js: build failed with ${report.errors} error(s). The page below is the last good build.`;
          title.style.cssText = 'color:#fbbf24;font-weight:600;margin-bottom:1rem';
          overlay.append(title);

          for (const line of report.diagnostics) {
            const row = document.createElement('pre');
            row.textContent = line;
            row.style.cssText = 'margin:0 0 .5rem;white-space:pre-wrap;border-left:3px solid #a78bfa;padding-left:.75rem';
            overlay.append(row);
          }

          const hint = document.createElement('div');
          hint.textContent = 'Fix the error and save; this page updates by itself.';
          hint.style.cssText = 'opacity:.6;margin-top:1rem';
          overlay.append(hint);

          document.body.append(overlay);
        }

        source.addEventListener('reload', () => location.reload());

        source.addEventListener('css', (event) => {
          clearOverlay();
          const changed = JSON.parse(event.data);
          let swapped = 0;
          for (const link of document.querySelectorAll('link[rel="stylesheet"]')) {
            const url = new URL(link.href, location.href);
            if (url.origin !== location.origin) continue;
            const path = url.pathname.replace(/^\//, '');
            if (!changed.some((file) => path === file || path.endsWith('/' + file))) continue;
            url.searchParams.set('csjs', Date.now().toString());
            link.href = url.href;
            swapped++;
          }
          // A stylesheet the page does not link (an @import, a CSS module) cannot be swapped.
          if (swapped === 0) location.reload();
        });

        source.addEventListener('build-error', (event) => showOverlay(JSON.parse(event.data)));
        """;
}
