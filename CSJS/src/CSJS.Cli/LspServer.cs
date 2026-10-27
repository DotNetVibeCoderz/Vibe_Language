using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CSJS.LanguageService;

namespace CSJS.Cli;

/// <summary>
/// <c>cs2js lsp</c>: the CSJS language server.
/// <para>
/// A thin JSON-RPC shell over <see cref="CsjsLanguageService"/>, which does the actual work and
/// is shared with CSJSGen. Any LSP client — VS Code, Neovim, Rider through LSP4IJ, Visual Studio —
/// gets the same C# IntelliSense, with the transpiler's own diagnostics inline.
/// </para>
/// <para>
/// Notifications are handled in arrival order on the read loop, so a request always sees every
/// edit sent before it. Requests run concurrently and can be cancelled with
/// <c>$/cancelRequest</c>. Diagnostics are pushed after a short quiet period rather than on every
/// keystroke, because each push transpiles the whole project.
/// </para>
/// </summary>
public sealed class LspServer : IDisposable
{
    private const int MethodNotFound = -32601;
    private const int InvalidParams = -32602;
    private const int InternalError = -32603;
    private const int ServerNotInitialized = -32002;
    private const int RequestCancelled = -32800;

    private readonly LspTransport _transport;
    private readonly TextWriter _log;
    private readonly LspProjects _projects = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _diagnosticRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _published = new(StringComparer.OrdinalIgnoreCase);

    private bool _initialized;
    private bool _shutdownRequested;
    private bool _labelOffsets;

    /// <summary>Quiet period before diagnostics are recomputed and pushed.</summary>
    public TimeSpan DiagnosticsDelay { get; init; } = TimeSpan.FromMilliseconds(350);

    public LspServer(Stream input, Stream output, TextWriter? log = null)
    {
        _transport = new LspTransport(input, output);
        _log = log ?? TextWriter.Null;
    }

    /// <summary>
    /// Runs over the process's own stdio. Anything else writing to stdout would corrupt the
    /// protocol stream, so console output is redirected to stderr for the session.
    /// </summary>
    public static int RunStdio()
    {
        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();
        var log = Console.Error;
        Console.SetOut(log);

        using var server = new LspServer(input, output, log);
        return server.RunAsync().GetAwaiter().GetResult();
    }

    /// <summary>Serves until <c>exit</c> or end of input. Returns the process exit code.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        while (await _transport.ReadAsync(cancellationToken).ConfigureAwait(false) is { } message)
        {
            var method = message["method"]?.GetValue<string>();
            var id = message["id"];
            var parameters = message["params"];

            // A message with no method is a response to a request this server sent; it sends none.
            if (method is null) continue;

            if (id is null)
            {
                if (method == "exit") return _shutdownRequested ? 0 : 1;

                try
                {
                    HandleNotification(method, parameters);
                }
                catch (Exception ex)
                {
                    _log.WriteLine($"cs2js lsp: {method} failed: {ex.Message}");
                }

                continue;
            }

            // The lifecycle requests are answered inline: nothing may overtake initialize.
            if (method == "initialize")
            {
                await RespondAsync(id, Initialize(parameters)).ConfigureAwait(false);
                continue;
            }

            if (method == "shutdown")
            {
                _shutdownRequested = true;
                await RespondAsync(id, null).ConfigureAwait(false);
                continue;
            }

            if (!_initialized)
            {
                await RespondErrorAsync(id, ServerNotInitialized, "The server has not been initialized.").ConfigureAwait(false);
                continue;
            }

            var key = id.ToJsonString();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pending[key] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await HandleRequestAsync(method, parameters, cts.Token).ConfigureAwait(false);
                    await RespondAsync(id, result).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await RespondErrorAsync(id, RequestCancelled, "Request cancelled.").ConfigureAwait(false);
                }
                catch (MethodNotFoundException)
                {
                    await RespondErrorAsync(id, MethodNotFound, $"Unhandled method {method}.").ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    await RespondErrorAsync(id, InvalidParams, ex.Message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.WriteLine($"cs2js lsp: {method} failed: {ex}");
                    await RespondErrorAsync(id, InternalError, ex.Message).ConfigureAwait(false);
                }
                finally
                {
                    if (_pending.TryRemove(key, out var done)) done.Dispose();
                }
            }, CancellationToken.None);
        }

        return _shutdownRequested ? 0 : 1;
    }

    // -----------------------------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------------------------

    private JsonObject Initialize(JsonNode? parameters)
    {
        _initialized = true;

        _labelOffsets = parameters?["capabilities"]?["textDocument"]?["signatureHelp"]?
            ["signatureInformation"]?["parameterInformation"]?["labelOffsetSupport"]?.GetValue<bool>() ?? false;

        // Loading the root project up front means the first file opened already sees its
        // siblings, instead of reporting their types as missing until they are opened too.
        foreach (var folder in WorkspaceFolders(parameters))
        {
            try
            {
                if (_projects.LoadIfProject(folder) is { } project) ScheduleDiagnostics(project);
            }
            catch (Exception ex)
            {
                _log.WriteLine($"cs2js lsp: could not load {folder}: {ex.Message}");
            }
        }

        var version = typeof(LspServer).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

        return new JsonObject
        {
            ["capabilities"] = new JsonObject
            {
                ["positionEncoding"] = "utf-16",
                ["textDocumentSync"] = new JsonObject
                {
                    ["openClose"] = true,
                    ["change"] = 1, // full: the documents are small and it cannot drift
                    ["save"] = new JsonObject { ["includeText"] = true },
                },
                ["completionProvider"] = new JsonObject
                {
                    ["triggerCharacters"] = new JsonArray(".", "<", "["),
                    ["resolveProvider"] = true,
                },
                ["hoverProvider"] = true,
                ["signatureHelpProvider"] = new JsonObject
                {
                    ["triggerCharacters"] = new JsonArray("(", ","),
                    ["retriggerCharacters"] = new JsonArray(")"),
                },
                ["definitionProvider"] = true,
            },
            ["serverInfo"] = new JsonObject { ["name"] = "cs2js", ["version"] = version },
        };
    }

    private static IEnumerable<string> WorkspaceFolders(JsonNode? parameters)
    {
        if (parameters?["workspaceFolders"] is JsonArray folders && folders.Count > 0)
        {
            foreach (var folder in folders)
            {
                if (folder?["uri"]?.GetValue<string>() is { } uri && ToPath(uri) is { } path)
                    yield return path;
            }
            yield break;
        }

        if (parameters?["rootUri"]?.GetValue<string>() is { } rootUri && ToPath(rootUri) is { } root)
            yield return root;
        else if (parameters?["rootPath"]?.GetValue<string>() is { Length: > 0 } rootPath)
            yield return rootPath;
    }

    // -----------------------------------------------------------------------------------------
    // Document sync
    // -----------------------------------------------------------------------------------------

    private void HandleNotification(string method, JsonNode? parameters)
    {
        switch (method)
        {
            case "initialized":
                break;

            case "$/cancelRequest":
                if (parameters?["id"] is { } cancelled && _pending.TryGetValue(cancelled.ToJsonString(), out var cts))
                    cts.Cancel();
                break;

            case "textDocument/didOpen":
            {
                var document = parameters?["textDocument"];
                var uri = document?["uri"]?.GetValue<string>();
                var text = document?["text"]?.GetValue<string>();
                if (uri is null || text is null || ToPath(uri) is not { } path || !IsCSharp(path)) break;

                _projects.MarkOpen(path, uri);
                var project = _projects.ForDocument(path);
                project.Service.UpdateDocument(path, text);
                ScheduleDiagnostics(project);
                break;
            }

            case "textDocument/didChange":
            {
                var uri = parameters?["textDocument"]?["uri"]?.GetValue<string>();
                if (uri is null || ToPath(uri) is not { } path) break;

                // Full sync: the last change carries the whole text.
                if (parameters?["contentChanges"] is not JsonArray { Count: > 0 } changes) break;
                if (changes[^1]?["text"]?.GetValue<string>() is not { } text) break;

                var project = _projects.Find(path) ?? _projects.ForDocument(path);
                project.Service.UpdateDocument(path, text);
                ScheduleDiagnostics(project);
                break;
            }

            case "textDocument/didSave":
            {
                var uri = parameters?["textDocument"]?["uri"]?.GetValue<string>();
                if (uri is null || ToPath(uri) is not { } path) break;

                if (Path.GetFileName(path).Equals(CsjsProject.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    ReloadProject(Path.GetDirectoryName(path)!);
                    break;
                }

                if (parameters?["text"]?.GetValue<string>() is { } text && _projects.Find(path) is { } project)
                {
                    project.Service.UpdateDocument(path, text);
                    ScheduleDiagnostics(project);
                }
                break;
            }

            case "textDocument/didClose":
            {
                var uri = parameters?["textDocument"]?["uri"]?.GetValue<string>();
                if (uri is null || ToPath(uri) is not { } path) break;

                _projects.MarkClosed(path);
                if (_projects.Find(path) is not { } project) break;

                // A project source goes back to what is on disk, which is what the build sees;
                // anything else was only in the workspace because it was open.
                if (LspProjects.IsProjectSource(project, path) && File.Exists(path))
                {
                    project.Service.UpdateDocument(path, File.ReadAllText(path));
                }
                else
                {
                    project.Service.RemoveDocument(path);
                    _ = PublishAsync(path, Array.Empty<LanguageDiagnostic>());
                }

                ScheduleDiagnostics(project);
                break;
            }

            case "workspace/didChangeWatchedFiles":
                if (parameters?["changes"] is JsonArray fileChanges) OnWatchedFiles(fileChanges);
                break;
        }
    }

    /// <summary>Files changed outside the editor: a git checkout, a generator, another tool.</summary>
    private void OnWatchedFiles(JsonArray changes)
    {
        var touched = new HashSet<LspProject>();

        foreach (var change in changes)
        {
            if (change?["uri"]?.GetValue<string>() is not { } uri || ToPath(uri) is not { } path) continue;
            var type = change["type"]?.GetValue<int>() ?? 2;

            if (Path.GetFileName(path).Equals(CsjsProject.FileName, StringComparison.OrdinalIgnoreCase))
            {
                ReloadProject(Path.GetDirectoryName(path)!);
                continue;
            }

            if (!IsCSharp(path) || _projects.IsOpen(path)) continue;

            var project = _projects.ForDocument(path);
            if (type == 3 || !File.Exists(path))
                project.Service.RemoveDocument(path);
            else if (LspProjects.IsProjectSource(project, path))
                project.Service.UpdateDocument(path, File.ReadAllText(path));

            touched.Add(project);
        }

        foreach (var project in touched) ScheduleDiagnostics(project);
    }

    /// <summary>Reloads a project after its <c>csjs.json</c> changed, keeping open buffers.</summary>
    private void ReloadProject(string directory)
    {
        var open = _projects.All
            .Where(p => p.Directory.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Service.DocumentPaths.Where(_projects.IsOpen).Select(d => (Path: d, Text: p.Service.GetText(d))))
            .ToList();

        _projects.Unload(directory);

        LspProject? reloaded = _projects.LoadIfProject(directory);
        foreach (var (path, text) in open)
        {
            if (text is null) continue;
            reloaded ??= _projects.ForDocument(path);
            reloaded.Service.UpdateDocument(path, text);
        }

        if (reloaded is not null) ScheduleDiagnostics(reloaded);
    }

    private static bool IsCSharp(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".csjs";

    // -----------------------------------------------------------------------------------------
    // Diagnostics
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Recomputes a project's diagnostics after a quiet period. A newer edit cancels the pending
    /// run, so a burst of typing costs one transpile, not one per keystroke.
    /// </summary>
    private void ScheduleDiagnostics(LspProject project)
    {
        var cts = new CancellationTokenSource();
        if (_diagnosticRuns.TryGetValue(project.Directory, out var previous)) previous.Cancel();
        _diagnosticRuns[project.Directory] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DiagnosticsDelay, cts.Token).ConfigureAwait(false);

                var version = project.Service.Version;
                var diagnostics = await project.Service.GetDiagnosticsAsync(null, cts.Token).ConfigureAwait(false);

                // An edit landed while this ran; its own run will publish fresher results.
                if (project.Service.Version != version || cts.IsCancellationRequested) return;

                var byPath = diagnostics
                    .GroupBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<LanguageDiagnostic>)g.ToList(), StringComparer.OrdinalIgnoreCase);

                var previouslyPublished = _published.GetOrAdd(project.Directory, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

                HashSet<string> stale;
                lock (previouslyPublished)
                {
                    stale = previouslyPublished.Where(p => !byPath.ContainsKey(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    previouslyPublished.Clear();
                    previouslyPublished.UnionWith(byPath.Keys);
                }

                // Open documents always get a publish, even an empty one, so fixing the last
                // error in a file clears it rather than leaving it underlined.
                foreach (var path in project.Service.DocumentPaths.Where(_projects.IsOpen))
                {
                    if (!byPath.ContainsKey(path)) stale.Add(path);
                }

                foreach (var (path, list) in byPath) await PublishAsync(path, list).ConfigureAwait(false);
                foreach (var path in stale) await PublishAsync(path, Array.Empty<LanguageDiagnostic>()).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer edit.
            }
            catch (Exception ex)
            {
                _log.WriteLine($"cs2js lsp: diagnostics failed: {ex.Message}");
            }
        });
    }

    private Task PublishAsync(string path, IReadOnlyList<LanguageDiagnostic> diagnostics)
    {
        var array = new JsonArray();
        foreach (var diagnostic in diagnostics)
        {
            array.Add(new JsonObject
            {
                ["range"] = Range(diagnostic.Range),
                ["severity"] = (int)diagnostic.Severity,
                ["code"] = diagnostic.Code,
                ["source"] = diagnostic.Source,
                ["message"] = diagnostic.Message,
            });
        }

        return NotifyAsync("textDocument/publishDiagnostics", new JsonObject
        {
            ["uri"] = _projects.UriFor(path),
            ["diagnostics"] = array,
        });
    }

    // -----------------------------------------------------------------------------------------
    // Requests
    // -----------------------------------------------------------------------------------------

    private async Task<JsonNode?> HandleRequestAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "textDocument/completion":
            {
                var (project, path, position) = Locate(parameters);
                if (project is null) return null;

                var result = await project.Service.GetCompletionsAsync(path, position, cancellationToken).ConfigureAwait(false);
                if (result is null) return null;

                var items = new JsonArray();
                foreach (var item in result.Items)
                {
                    var json = new JsonObject
                    {
                        ["label"] = item.Label,
                        ["kind"] = (int)item.Kind,
                        ["textEdit"] = new JsonObject
                        {
                            ["range"] = Range(item.ReplaceRange),
                            ["newText"] = item.InsertText,
                        },
                        ["data"] = new JsonObject
                        {
                            ["project"] = project.Directory,
                            ["resultId"] = result.ResultId,
                            ["index"] = item.Index,
                        },
                    };
                    if (item.Detail is not null) json["detail"] = item.Detail;
                    if (item.SortText is not null) json["sortText"] = item.SortText;
                    if (item.FilterText is not null) json["filterText"] = item.FilterText;
                    if (item.Preselect) json["preselect"] = true;
                    items.Add(json);
                }

                return new JsonObject { ["isIncomplete"] = false, ["items"] = items };
            }

            case "completionItem/resolve":
            {
                if (parameters is not JsonObject item) throw new ArgumentException("Expected a completion item.");
                var resolved = (JsonObject)item.DeepClone();

                var data = item["data"];
                var directory = data?["project"]?.GetValue<string>();
                var project = _projects.All.FirstOrDefault(p => p.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase));
                if (project is null || data?["resultId"] is null || data["index"] is null) return resolved;

                var details = await project.Service
                    .ResolveCompletionAsync(data["resultId"]!.GetValue<int>(), data["index"]!.GetValue<int>(), cancellationToken)
                    .ConfigureAwait(false);

                if (details?.Detail is { } detail) resolved["detail"] = detail;
                if (details?.Documentation is { } documentation)
                    resolved["documentation"] = new JsonObject { ["kind"] = "markdown", ["value"] = documentation };

                return resolved;
            }

            case "textDocument/hover":
            {
                var (project, path, position) = Locate(parameters);
                if (project is null) return null;

                var hover = await project.Service.GetHoverAsync(path, position, cancellationToken).ConfigureAwait(false);
                if (hover is null) return null;

                return new JsonObject
                {
                    ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = hover.Markdown },
                    ["range"] = Range(hover.Range),
                };
            }

            case "textDocument/signatureHelp":
            {
                var (project, path, position) = Locate(parameters);
                if (project is null) return null;

                var help = await project.Service.GetSignatureHelpAsync(path, position, cancellationToken).ConfigureAwait(false);
                if (help is null) return null;

                var signatures = new JsonArray();
                foreach (var signature in help.Signatures)
                {
                    var parametersJson = new JsonArray();
                    foreach (var parameter in signature.Parameters)
                    {
                        var json = new JsonObject
                        {
                            ["label"] = _labelOffsets
                                ? new JsonArray(parameter.Start, parameter.End)
                                : JsonValue.Create(parameter.Label),
                        };
                        if (parameter.Documentation is not null) json["documentation"] = parameter.Documentation;
                        parametersJson.Add(json);
                    }

                    var signatureJson = new JsonObject
                    {
                        ["label"] = signature.Label,
                        ["parameters"] = parametersJson,
                    };
                    if (signature.Documentation is not null) signatureJson["documentation"] = signature.Documentation;
                    signatures.Add(signatureJson);
                }

                return new JsonObject
                {
                    ["signatures"] = signatures,
                    ["activeSignature"] = help.ActiveSignature,
                    ["activeParameter"] = help.ActiveParameter,
                };
            }

            case "textDocument/definition":
            {
                var (project, path, position) = Locate(parameters);
                if (project is null) return null;

                var locations = await project.Service.GetDefinitionAsync(path, position, cancellationToken).ConfigureAwait(false);
                var array = new JsonArray();
                foreach (var location in locations)
                {
                    array.Add(new JsonObject
                    {
                        ["uri"] = _projects.UriFor(location.Path),
                        ["range"] = Range(location.Range),
                    });
                }

                return array;
            }

            default:
                throw new MethodNotFoundException();
        }
    }

    private (LspProject? Project, string Path, TextPosition Position) Locate(JsonNode? parameters)
    {
        var uri = parameters?["textDocument"]?["uri"]?.GetValue<string>()
                  ?? throw new ArgumentException("textDocument.uri is required.");
        var position = parameters!["position"] ?? throw new ArgumentException("position is required.");

        var path = ToPath(uri) ?? throw new ArgumentException($"Unsupported document URI {uri}.");
        var line = position["line"]?.GetValue<int>() ?? 0;
        var character = position["character"]?.GetValue<int>() ?? 0;

        return (_projects.Find(path), path, new TextPosition(line, character));
    }

    // -----------------------------------------------------------------------------------------
    // Wire helpers
    // -----------------------------------------------------------------------------------------

    private Task RespondAsync(JsonNode id, JsonNode? result)
        => _transport.WriteAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        });

    private Task RespondErrorAsync(JsonNode id, int code, string message)
        => _transport.WriteAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        });

    private Task NotifyAsync(string method, JsonNode parameters)
        => _transport.WriteAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
        });

    private static JsonObject Range(TextRange range) => new()
    {
        ["start"] = new JsonObject { ["line"] = range.Start.Line, ["character"] = range.Start.Character },
        ["end"] = new JsonObject { ["line"] = range.End.Line, ["character"] = range.End.Character },
    };

    /// <summary>A local path for a <c>file:</c> URI; other schemes (untitled, git) are not served.</summary>
    /// <remarks>
    /// Parsed by hand because <see cref="Uri.LocalPath"/> gets VS Code's spelling wrong: it
    /// percent-encodes the drive colon (<c>file:///c%3A/src</c>), and <see cref="Uri"/> then
    /// yields <c>/c:/src</c>, which is no path at all.
    /// </remarks>
    internal static string? ToPath(string uri)
    {
        if (!uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;

        var rest = uri["file:".Length..];
        var host = "";
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            rest = rest[2..];
            var slash = rest.IndexOf('/');
            host = slash < 0 ? rest : rest[..slash];
            rest = slash < 0 ? "/" : rest[slash..];
        }

        var path = Uri.UnescapeDataString(rest);
        var remote = host.Length > 0 && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            if (path.Length >= 3 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':') path = path[1..];
            path = path.Replace('/', '\\');
            if (remote) path = @"\\" + host + path;
        }
        else if (remote)
        {
            path = "//" + host + path;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var cts in _diagnosticRuns.Values) cts.Cancel();
        _projects.Dispose();
    }

    private sealed class MethodNotFoundException : Exception;
}
