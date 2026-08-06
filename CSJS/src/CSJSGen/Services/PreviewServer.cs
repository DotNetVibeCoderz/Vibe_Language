using System.Diagnostics;
using System.Net;
using System.Text;
using CSJS.Cli;

namespace CSJSGen.Services;

/// <summary>
/// Builds the open project and serves it over HTTP so it can be opened in a real browser.
///
/// Running on Node answers "does this code work"; it cannot answer "does this page work", because
/// Node has no DOM — a project that touches <c>document</c> fails there by construction. This is
/// the other half: the same build the CLI performs, served, with the browser pointed at it.
///
/// The server outlives a single run. Rebuilding writes into the directory it is already serving,
/// so a second run refreshes what the open tab will fetch instead of stranding it on a dead port.
/// </summary>
public sealed class PreviewServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private string _root = "";

    /// <summary>The address currently being served, or null when nothing is.</summary>
    public string? Address { get; private set; }

    public int Port { get; private set; }

    /// <summary>
    /// Builds <paramref name="projectDirectory"/> and makes sure it is being served. Returns the
    /// report and the URL to open; the URL is null when the build failed.
    /// </summary>
    public (BuildReport Report, string? Url) BuildAndServe(string projectDirectory)
    {
        var project = CsjsProject.Load(projectDirectory);

        // The entry point call is what makes the page do something once loaded; without it the
        // module defines classes and the page stays blank.
        var report = new BuildRunner(project, projectDirectory, emitEntryPointCall: true).Run(quiet: true);
        if (!report.Success) return (report, null);

        var outputRoot = project.ResolveOutputRoot(projectDirectory);

        // The host page has to answer "/" whatever it is called in the project.
        EnsureIndex(project, projectDirectory, outputRoot);

        if (_listener is null || !_listener.IsListening || !PathsMatch(_root, outputRoot))
        {
            Stop();
            Start(outputRoot);
        }

        return (report, Address);
    }

    /// <summary>
    /// Copies the configured host page to <c>index.html</c> at the output root when it is not
    /// already there — a page kept in <c>wwwroot/</c> or under another name still has to be what
    /// the server returns for "/".
    /// </summary>
    private static void EnsureIndex(CsjsProject project, string projectDirectory, string outputRoot)
    {
        var target = Path.Combine(outputRoot, "index.html");
        if (File.Exists(target)) return;

        var configured = project.Html is { Length: > 0 } html
            ? Path.GetFullPath(Path.Combine(projectDirectory, html))
            : null;

        var source = configured is not null && File.Exists(configured)
            ? configured
            : new[] { "index.html", "wwwroot/index.html", "public/index.html" }
                .Select(c => Path.Combine(projectDirectory, c))
                .FirstOrDefault(File.Exists);

        if (source is not null) File.Copy(source, target, overwrite: true);
        else File.WriteAllText(target, FallbackPage(project.Name ?? "CSJS"));
    }

    private void Start(string outputRoot)
    {
        _root = outputRoot;
        Port = FreePort();

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();

        Address = $"http://localhost:{Port}/";
        _cancellation = new CancellationTokenSource();

        var listener = _listener;
        var token = _cancellation.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }

                try { Serve(context, _root); }
                catch { /* one bad request must not take the server down */ }
            }
        }, token);
    }

    /// <summary>Asks the OS for an unused port rather than guessing one that may be taken.</summary>
    private static int FreePort()
    {
        var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    private static void Serve(HttpListenerContext context, string root)
    {
        var relative = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "/").TrimStart('/');
        if (relative.Length == 0) relative = "index.html";

        var path = Path.GetFullPath(Path.Combine(root, relative));

        // Refuse anything that escapes the served directory.
        if (!path.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            context.Response.StatusCode = 404;
            var message = Encoding.UTF8.GetBytes($"404 — {relative} tidak ditemukan.");
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.ContentLength64 = message.Length;
            context.Response.OutputStream.Write(message);
            context.Response.Close();
            return;
        }

        context.Response.ContentType = ContentTypeOf(Path.GetExtension(path));

        // A preview is rebuilt constantly; a cached asset would hide the change being previewed.
        context.Response.Headers["Cache-Control"] = "no-store";

        var bytes = File.ReadAllBytes(path);
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
    }

    /// <summary>
    /// Content types for what a project actually ships. A wrong type on a module is fatal —
    /// browsers refuse to execute one that is not JavaScript — so this covers more than the
    /// obvious few.
    /// </summary>
    private static string ContentTypeOf(string extension) => extension.ToLowerInvariant() switch
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
        ".avif" => "image/avif",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".pdf" => "application/pdf",
        ".wasm" => "application/wasm",
        ".txt" or ".md" or ".csjs" or ".cs" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    /// <summary>Opens the system browser. Windows needs the shell to resolve the default one.</summary>
    public static void OpenBrowser(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static bool PathsMatch(string a, string b)
        => string.Equals(a.TrimEnd(Path.DirectorySeparatorChar),
                         b.TrimEnd(Path.DirectorySeparatorChar),
                         StringComparison.OrdinalIgnoreCase);

    private static string FallbackPage(string name) => $"""
        <!doctype html>
        <html lang="id">
        <head>
          <meta charset="utf-8" />
          <title>{name}</title>
        </head>
        <body>
          <h1>{name}</h1>
          <script type="module" src="Program.js"></script>
        </body>
        </html>
        """;

    public void Stop()
    {
        _cancellation?.Cancel();
        try { _listener?.Stop(); } catch { /* already down */ }
        _listener?.Close();
        _listener = null;
        Address = null;
    }

    public void Dispose() => Stop();
}
