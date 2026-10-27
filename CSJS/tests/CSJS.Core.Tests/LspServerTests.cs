using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using CSJS.Cli;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// End to end: launches the real <c>cs2js lsp</c> process and talks JSON-RPC to it over stdio,
/// the way VS Code does. Unit tests of the language service cannot catch a broken framing layer,
/// a stray write to stdout, or a URI an editor sends in a spelling the server does not expect —
/// this does.
/// </summary>
public sealed class LspServerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private readonly string _project;

    public LspServerTests()
    {
        _project = Path.Combine(Path.GetTempPath(), "csjs-lsp-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(Path.Combine(_project, "src"));

        File.WriteAllText(Path.Combine(_project, "csjs.json"), """{ "name": "LspSample" }""");
        File.WriteAllText(Path.Combine(_project, "src", "Models.cs"), """
            public sealed class Greeter
            {
                public string Greet(string name) => "Hello, " + name;
            }
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_project, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ServesAScriptedSessionOverStdio()
    {
        var programPath = Path.Combine(_project, "src", "Program.csjs");
        var source = """
            using System;

            public static class App
            {
                public static void Main()
                {
                    var greeting = new Greeter().Greet("csjs");
                    Console.WriteLine(greeting.ToUpperInvariant());
                    Console.Beep();
                }
            }
            """;
        File.WriteAllText(programPath, source);

        await using var client = LspClient.Start();

        // VS Code percent-encodes the drive colon; the server has to accept that spelling and
        // answer in it, or diagnostics land on a document the editor does not recognise.
        var programUri = EditorUri(programPath);

        var init = await client.RequestAsync("initialize", new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = EditorUri(_project),
            ["capabilities"] = new JsonObject(),
        });
        Assert.True(init["capabilities"]?["completionProvider"] is not null);
        Assert.Equal(true, init["capabilities"]?["hoverProvider"]?.GetValue<bool>());

        await client.NotifyAsync("initialized", new JsonObject());
        await client.NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = programUri,
                ["languageId"] = "csjs",
                ["version"] = 1,
                ["text"] = source,
            },
        });

        // Diagnostics are pushed: the transpiler's CSJS0002 for the unmapped Console.Beep.
        var published = await client.WaitForNotificationAsync("textDocument/publishDiagnostics",
            p => p["uri"]?.GetValue<string>() == programUri && p["diagnostics"] is JsonArray { Count: > 0 });
        var diagnostic = Assert.Single(published["diagnostics"]!.AsArray());
        Assert.Equal("CSJS0002", diagnostic!["code"]!.GetValue<string>());
        Assert.Equal(8, diagnostic["range"]!["start"]!["line"]!.GetValue<int>());

        // Hover on ToUpperInvariant: the C# signature and the JavaScript it becomes.
        var hover = await client.RequestAsync("textDocument/hover", At(programUri, line: 7, character: 40));
        var markdown = hover["contents"]!["value"]!.GetValue<string>();
        Assert.Contains("ToUpperInvariant", markdown);
        Assert.Contains("toUpperCase", markdown);

        // Go to definition crosses into a file that was never opened.
        var definition = await client.RequestAsync("textDocument/definition", At(programUri, line: 6, character: 38));
        var location = Assert.Single(definition.AsArray());
        Assert.EndsWith("Models.cs", Uri.UnescapeDataString(location!["uri"]!.GetValue<string>()), StringComparison.OrdinalIgnoreCase);

        // Type a member access and ask for completion, as an editor does on '.'.
        var edited = source.Replace("Console.Beep();", "CSJS.Browser.Document.", StringComparison.Ordinal);
        await client.NotifyAsync("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = programUri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = edited }),
        });

        var completion = await client.RequestAsync("textDocument/completion",
            At(programUri, line: 8, character: 8 + "CSJS.Browser.Document.".Length));
        var items = completion["items"]!.AsArray();
        var querySelector = items.FirstOrDefault(i => i!["label"]!.GetValue<string>() == "QuerySelector");
        Assert.NotNull(querySelector);

        var resolved = await client.RequestAsync("completionItem/resolve", querySelector!.DeepClone());
        Assert.Contains("QuerySelector(string selectors)", resolved["detail"]!.GetValue<string>());

        // Signature help inside the Greet call.
        var signature = await client.RequestAsync("textDocument/signatureHelp", At(programUri, line: 6, character: 44));
        Assert.Contains("Greet(string name)", signature["signatures"]![0]!["label"]!.GetValue<string>());

        await client.RequestAsync("shutdown", null);
        await client.NotifyAsync("exit", null);
        Assert.Equal(0, await client.WaitForExitAsync());
    }

    private static JsonObject At(string uri, int line, int character) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = uri },
        ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
    };

    private static string EditorUri(string path)
    {
        var full = Path.GetFullPath(path).Replace('\\', '/');
        if (OperatingSystem.IsWindows() && full.Length > 1 && full[1] == ':')
            full = "/" + char.ToLowerInvariant(full[0]) + "%3A" + full[2..];
        return "file://" + full.Replace(" ", "%20", StringComparison.Ordinal);
    }

    /// <summary>A minimal LSP client over a child process, built on the server's own framing.</summary>
    private sealed class LspClient : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly LspTransport _transport;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _responses = new();
        private readonly List<JsonObject> _notifications = new();
        private readonly SemaphoreSlim _notified = new(0);
        private readonly Task _reader;
        private int _nextId;

        private LspClient(Process process)
        {
            _process = process;
            _transport = new LspTransport(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
            _reader = Task.Run(ReadLoop);
        }

        public static LspClient Start()
        {
            // The referenced CLI project is copied beside the tests with its runtimeconfig.
            var cli = Path.Combine(AppContext.BaseDirectory, "cs2js.dll");
            Assert.True(File.Exists(cli), $"cs2js.dll not found at {cli}");

            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(cli);
            startInfo.ArgumentList.Add("lsp");

            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start cs2js.");
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            return new LspClient(process);
        }

        private async Task ReadLoop()
        {
            while (await _transport.ReadAsync() is { } message)
            {
                if (message["id"] is { } id && message["method"] is null)
                {
                    if (_responses.TryRemove(id.GetValue<int>(), out var pending)) pending.TrySetResult(message);
                    continue;
                }

                lock (_notifications) _notifications.Add(message);
                _notified.Release();
            }
        }

        public async Task<JsonNode> RequestAsync(string method, JsonNode? parameters)
        {
            var id = Interlocked.Increment(ref _nextId);
            var pending = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _responses[id] = pending;

            await _transport.WriteAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters,
            });

            var response = await pending.Task.WaitAsync(Timeout);
            Assert.True(response["error"] is null, $"{method} failed: {response["error"]?.ToJsonString()}");
            return response["result"] ?? JsonValue.Create("null")!;
        }

        public Task NotifyAsync(string method, JsonNode? parameters)
            => _transport.WriteAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters,
            });

        public async Task<JsonNode> WaitForNotificationAsync(string method, Func<JsonNode, bool> predicate)
        {
            var deadline = DateTime.UtcNow + Timeout;
            var seen = 0;
            while (true)
            {
                lock (_notifications)
                {
                    for (; seen < _notifications.Count; seen++)
                    {
                        var message = _notifications[seen];
                        if (message["method"]?.GetValue<string>() == method && message["params"] is { } p && predicate(p))
                            return p;
                    }
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !await _notified.WaitAsync(remaining))
                    throw new TimeoutException($"No matching {method} within {Timeout.TotalSeconds:F0} s.");
            }
        }

        public async Task<int> WaitForExitAsync()
        {
            using var cts = new CancellationTokenSource(Timeout);
            await _process.WaitForExitAsync(cts.Token);
            return _process.ExitCode;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            }

            try { await _reader.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* reader ends with the pipe */ }
            _process.Dispose();
        }
    }
}
