using CSJS.Cli;
using CSJS.LanguageService;

namespace CSJSGen.Services;

/// <summary>
/// The editor's C# IntelliSense: <see cref="CsjsLanguageService"/> — the same engine behind
/// <c>cs2js lsp</c> — kept in step with <see cref="ProjectService"/>.
/// <para>
/// It holds exactly the documents a compile would: every C# file of the open project, or the
/// active file on its own. Defines and references come from <c>csjs.json</c>, read through the
/// CLI's own <see cref="CsjsProject"/>, so the editor binds what the build binds.
/// </para>
/// </summary>
public sealed class CodeIntelligence : IDisposable
{
    private static readonly TimeSpan DiagnosticsDelay = TimeSpan.FromMilliseconds(450);

    private readonly ProjectService _projects;
    private readonly object _gate = new();

    private CsjsLanguageService _service = new();
    private string? _projectDirectory;
    private CancellationTokenSource? _diagnosticsRun;

    /// <summary>
    /// Raised off the UI thread when fresh diagnostics for a document are ready. Subscribers
    /// marshal back themselves, the way every other service event here works.
    /// </summary>
    public event Action<string, IReadOnlyList<LanguageDiagnostic>>? DiagnosticsReady;

    public CodeIntelligence(ProjectService projects)
    {
        _projects = projects;
        _projects.Changed += OnProjectChanged;
        OnProjectChanged();
    }

    /// <summary>The service, synchronised with the project. Never cache the result.</summary>
    public CsjsLanguageService Service
    {
        get { lock (_gate) return _service; }
    }

    /// <summary>The path IntelliSense answers for: the active file, when it is C#.</summary>
    public string? ActivePath => _projects.ActiveFile is { IsCsjs: true } file ? file.Path : null;

    /// <summary>
    /// Brings the active document up to the text the editor shows. Monaco reports changes after
    /// a short delay, so a completion request can otherwise land a keystroke behind.
    /// </summary>
    public void UpdateActive(string text)
    {
        if (ActivePath is { } path) Service.UpdateDocument(path, text);
    }

    /// <summary>Recomputes the active file's diagnostics once typing pauses.</summary>
    public void ScheduleDiagnostics()
    {
        var path = ActivePath;
        if (path is null) return;

        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _diagnosticsRun;
            _diagnosticsRun = cts;
        }
        previous?.Cancel();

        var service = Service;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DiagnosticsDelay, cts.Token);
                var version = service.Version;
                var diagnostics = await service.GetDiagnosticsAsync(path, cts.Token);

                // A newer edit has its own run coming; publishing this one would flicker.
                if (cts.IsCancellationRequested || service.Version != version) return;
                DiagnosticsReady?.Invoke(path, diagnostics);
            }
            catch (OperationCanceledException)
            {
                // Superseded.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"CSJSGen: live diagnostics failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Mirrors the project into the workspace. A different project directory gets a fresh
    /// service, because its <c>csjs.json</c> may define different symbols and references.
    /// </summary>
    private void OnProjectChanged()
    {
        var project = _projects.Current;
        var directory = project?.Directory;

        CsjsLanguageService service;
        lock (_gate)
        {
            if (!string.Equals(directory, _projectDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _service.Dispose();
                _service = new CsjsLanguageService(OptionsFor(directory));
                _projectDirectory = directory;
            }

            service = _service;
        }

        var documents = project is not null && project.Files.Any(f => f.IsCsjs)
            ? project.Files.Where(f => f.IsCsjs)
            : _projects.ActiveFile is { IsCsjs: true } active ? new[] { active } : Enumerable.Empty<ProjectFile>();

        service.SetDocuments(documents.Select(f => new KeyValuePair<string, string>(f.Path, f.Content)));
        ScheduleDiagnostics();
    }

    private static LanguageServiceOptions OptionsFor(string? directory)
    {
        var options = new LanguageServiceOptions();
        if (string.IsNullOrEmpty(directory) || !File.Exists(Path.Combine(directory, CsjsProject.FileName)))
            return options;

        try
        {
            var config = CsjsProject.Load(directory);
            options.AssemblyName = config.Name ?? options.AssemblyName;
            options.DefineConstants.AddRange(config.Define);
            options.AdditionalReferences.AddRange(
                config.References.Select(r => Path.GetFullPath(Path.Combine(directory, r))));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // A malformed csjs.json still leaves plain C# IntelliSense working.
        }

        return options;
    }

    public void Dispose()
    {
        _projects.Changed -= OnProjectChanged;
        lock (_gate)
        {
            _diagnosticsRun?.Cancel();
            _service.Dispose();
        }
    }
}
