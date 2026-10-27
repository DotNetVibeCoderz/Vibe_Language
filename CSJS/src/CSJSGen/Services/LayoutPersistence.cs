using CSJSGen.Models;

namespace CSJSGen.Services;

/// <summary>
/// Writes the workspace layout back to <c>app.config</c>, so the next session opens the way
/// this one was left.
/// <para>
/// <see cref="AppState.Changed"/> fires for far more than layout — compiling, output text, every
/// splitter step while dragging — so saves are debounced and skipped when nothing that is
/// persisted actually differs. <see cref="Flush"/> writes immediately and is called on close,
/// so a change made in the last half second is not lost.
/// </para>
/// </summary>
public sealed class LayoutPersistence : IDisposable
{
    private readonly AppState _state;
    private readonly Action<WorkspaceLayout, string> _save;
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();

    private WorkspaceLayout _saved;
    private string _savedTheme;

    /// <summary>How long the layout has to stay put before it is written.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(600);

    public LayoutPersistence(AppState state, Action<WorkspaceLayout, string>? save = null)
    {
        _state = state;
        _save = save ?? AppSettings.SaveLayout;

        // What the state holds now is what was restored, so it does not need writing back.
        _saved = state.CaptureLayout();
        _savedTheme = state.Theme;

        _timer = new System.Threading.Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        _state.Changed += OnChanged;
    }

    private void OnChanged() => _timer.Change(Debounce, Timeout.InfiniteTimeSpan);

    /// <summary>Writes the layout now if it differs from what was last written.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            var layout = _state.CaptureLayout();
            var theme = _state.Theme;
            if (layout == _saved && theme == _savedTheme) return;

            try
            {
                _save(layout, theme);
                _saved = layout;
                _savedTheme = theme;
            }
            catch
            {
                // A read-only install folder must not break resizing; the layout simply stays
                // in memory for this session.
            }
        }
    }

    public void Dispose()
    {
        _state.Changed -= OnChanged;
        _timer.Dispose();
    }
}
