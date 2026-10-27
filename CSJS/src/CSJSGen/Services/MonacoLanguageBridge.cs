using CSJS.LanguageService;
using Microsoft.JSInterop;

namespace CSJSGen.Services;

/// <summary>
/// The JavaScript-facing half of IntelliSense: Monaco's completion, hover, and signature-help
/// providers call these through a <see cref="DotNetObjectReference{T}"/>.
/// <para>
/// The methods are instance methods on purpose — a static <c>[JSInvokable]</c> reached through an
/// object reference fails silently. Each request carries Monaco's current text, so the answer is
/// never computed against a buffer one keystroke stale. Monaco is one-based and the service is
/// zero-based; the conversion happens here and nowhere else.
/// </para>
/// <para>
/// The work runs on the thread pool, never on the UI thread the interop call arrives on. Interop
/// cannot carry Monaco's cancellation token across, so each kind of request cancels its own
/// predecessor instead: typing fast leaves only the newest completion running.
/// </para>
/// </summary>
public sealed class MonacoLanguageBridge : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly CodeIntelligence _intelligence;
    private readonly Dictionary<string, CancellationTokenSource> _inFlight = new();

    public MonacoLanguageBridge(CodeIntelligence intelligence) => _intelligence = intelligence;

    [JSInvokable]
    public async Task<MonacoCompletionList?> Complete(string text, int lineNumber, int column)
    {
        if (Prepare(text) is not { } path) return null;

        using var lease = Begin("completion");
        var service = _intelligence.Service;
        var token = lease.Token;
        var result = await Task.Run(() => service.GetCompletionsAsync(path, ToPosition(lineNumber, column), token), token);
        if (result is null) return null;

        return new MonacoCompletionList(
            result.ResultId,
            result.Items.Select(i => new MonacoCompletionItem(
                i.Label,
                i.InsertText,
                i.FilterText,
                i.SortText,
                i.Detail,
                i.Kind.ToString(),
                ToMonaco(i.ReplaceRange),
                i.Index,
                i.Preselect)).ToList());
    }

    [JSInvokable]
    public async Task<CompletionDetails?> ResolveCompletion(int resultId, int index)
    {
        using var lease = Begin("resolve");
        var service = _intelligence.Service;
        var token = lease.Token;
        return await Task.Run(() => service.ResolveCompletionAsync(resultId, index, token), token);
    }

    [JSInvokable]
    public async Task<MonacoHover?> Hover(string text, int lineNumber, int column)
    {
        if (Prepare(text) is not { } path) return null;

        using var lease = Begin("hover");
        var service = _intelligence.Service;
        var token = lease.Token;
        var hover = await Task.Run(() => service.GetHoverAsync(path, ToPosition(lineNumber, column), token), token);
        return hover is null ? null : new MonacoHover(hover.Markdown, ToMonaco(hover.Range));
    }

    [JSInvokable]
    public async Task<MonacoSignatureHelp?> SignatureHelp(string text, int lineNumber, int column)
    {
        if (Prepare(text) is not { } path) return null;

        using var lease = Begin("signature");
        var service = _intelligence.Service;
        var token = lease.Token;
        var help = await Task.Run(() => service.GetSignatureHelpAsync(path, ToPosition(lineNumber, column), token), token);
        if (help is null) return null;

        return new MonacoSignatureHelp(
            help.Signatures.Select(s => new MonacoSignature(
                s.Label,
                s.Documentation,
                s.Parameters.Select(p => new MonacoParameter(new[] { p.Start, p.End }, p.Documentation)).ToList())).ToList(),
            help.ActiveSignature,
            help.ActiveParameter);
    }

    /// <summary>Syncs the text and returns the document path, or null when the file is not C#.</summary>
    private string? Prepare(string text)
    {
        var path = _intelligence.ActivePath;
        if (path is null) return null;

        _intelligence.UpdateActive(text);
        return path;
    }

    private Lease Begin(string kind)
    {
        var cts = new CancellationTokenSource(RequestTimeout);
        lock (_inFlight)
        {
            if (_inFlight.TryGetValue(kind, out var previous)) previous.Cancel();
            _inFlight[kind] = cts;
        }
        return new Lease(this, kind, cts);
    }

    private void End(string kind, CancellationTokenSource cts)
    {
        lock (_inFlight)
        {
            if (_inFlight.TryGetValue(kind, out var current) && current == cts) _inFlight.Remove(kind);
        }
        cts.Dispose();
    }

    private static TextPosition ToPosition(int lineNumber, int column)
        => new(Math.Max(0, lineNumber - 1), Math.Max(0, column - 1));

    internal static MonacoRange ToMonaco(TextRange range)
        => new(range.Start.Line + 1, range.Start.Character + 1, range.End.Line + 1, range.End.Character + 1);

    public void Dispose()
    {
        lock (_inFlight)
        {
            foreach (var cts in _inFlight.Values) cts.Cancel();
            _inFlight.Clear();
        }
    }

    private readonly struct Lease : IDisposable
    {
        private readonly MonacoLanguageBridge _owner;
        private readonly string _kind;
        private readonly CancellationTokenSource _cts;

        public Lease(MonacoLanguageBridge owner, string kind, CancellationTokenSource cts)
        {
            _owner = owner;
            _kind = kind;
            _cts = cts;
        }

        public CancellationToken Token => _cts.Token;

        public void Dispose() => _owner.End(_kind, _cts);
    }
}

// Shapes Monaco consumes directly; Blazor serialises them camelCase.

public sealed record MonacoRange(int StartLineNumber, int StartColumn, int EndLineNumber, int EndColumn);

public sealed record MonacoCompletionItem(
    string Label,
    string InsertText,
    string? FilterText,
    string? SortText,
    string? Detail,
    string Kind,
    MonacoRange Range,
    int Index,
    bool Preselect);

public sealed record MonacoCompletionList(int ResultId, IReadOnlyList<MonacoCompletionItem> Items);

public sealed record MonacoHover(string Markdown, MonacoRange Range);

public sealed record MonacoParameter(int[] Label, string? Documentation);

public sealed record MonacoSignature(string Label, string? Documentation, IReadOnlyList<MonacoParameter> Parameters);

public sealed record MonacoSignatureHelp(IReadOnlyList<MonacoSignature> Signatures, int ActiveSignature, int ActiveParameter);
