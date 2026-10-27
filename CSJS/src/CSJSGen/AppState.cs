using CSJSGen.Models;

namespace CSJSGen;

/// <summary>
/// UI state that outlives any single component: panel visibility, theme, and the output pane.
/// Components subscribe to <see cref="Changed"/> rather than threading state through parameters.
/// </summary>
public sealed class AppState
{
    private bool _showExplorer = true;
    private bool _showProperties = true;
    private bool _showChat = true;
    private bool _showLineNumbers = true;
    private string _theme = "dark";

    // Panel widths in CSS pixels. Bounded so a drag cannot make a panel unusable or squeeze the
    // editor out of existence.
    private double _explorerWidth = WorkspaceLayout.ExplorerDefault;
    private double _propertiesWidth = WorkspaceLayout.PropertiesDefault;
    private double _chatWidth = WorkspaceLayout.ChatDefault;

    public event Action? Changed;

    public bool ShowExplorer
    {
        get => _showExplorer;
        set { _showExplorer = value; Changed?.Invoke(); }
    }

    public bool ShowProperties
    {
        get => _showProperties;
        set { _showProperties = value; Changed?.Invoke(); }
    }

    public bool ShowChat
    {
        get => _showChat;
        set { _showChat = value; Changed?.Invoke(); }
    }

    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set { _showLineNumbers = value; Changed?.Invoke(); }
    }

    public double ExplorerWidth
    {
        get => _explorerWidth;
        set { _explorerWidth = Math.Clamp(value, WorkspaceLayout.ExplorerMin, WorkspaceLayout.ExplorerMax); Changed?.Invoke(); }
    }

    public double PropertiesWidth
    {
        get => _propertiesWidth;
        set { _propertiesWidth = Math.Clamp(value, WorkspaceLayout.PropertiesMin, WorkspaceLayout.PropertiesMax); Changed?.Invoke(); }
    }

    public double ChatWidth
    {
        get => _chatWidth;
        set { _chatWidth = Math.Clamp(value, WorkspaceLayout.ChatMin, WorkspaceLayout.ChatMax); Changed?.Invoke(); }
    }

    public string Theme
    {
        get => _theme;
        set { _theme = value; Changed?.Invoke(); }
    }

    /// <summary>Which tab the output pane shows: output, javascript, or types.</summary>
    public string OutputTab { get; set; } = "output";

    /// <summary>Text shown in the output pane.</summary>
    public string OutputText { get; set; } = "";

    public string GeneratedJavaScript { get; set; } = "";
    public string GeneratedTypes { get; set; } = "";

    public bool IsCompiling { get; set; }

    public void Notify() => Changed?.Invoke();

    /// <summary>
    /// Restores the layout and theme a previous session saved. Runs before the first render, so
    /// it raises nothing — there is no one listening yet, and nothing changed from their view.
    /// </summary>
    public void Restore(WorkspaceLayout layout, string theme)
    {
        _showExplorer = layout.ShowExplorer;
        _showProperties = layout.ShowProperties;
        _showChat = layout.ShowChat;
        _showLineNumbers = layout.ShowLineNumbers;
        _explorerWidth = Math.Clamp(layout.ExplorerWidth, WorkspaceLayout.ExplorerMin, WorkspaceLayout.ExplorerMax);
        _propertiesWidth = Math.Clamp(layout.PropertiesWidth, WorkspaceLayout.PropertiesMin, WorkspaceLayout.PropertiesMax);
        _chatWidth = Math.Clamp(layout.ChatWidth, WorkspaceLayout.ChatMin, WorkspaceLayout.ChatMax);
        _theme = theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
    }

    /// <summary>The current layout, in the shape <c>app.config</c> stores.</summary>
    public WorkspaceLayout CaptureLayout() => new()
    {
        ShowExplorer = _showExplorer,
        ShowProperties = _showProperties,
        ShowChat = _showChat,
        ShowLineNumbers = _showLineNumbers,
        ExplorerWidth = _explorerWidth,
        PropertiesWidth = _propertiesWidth,
        ChatWidth = _chatWidth,
    };

    public void ToggleTheme()
    {
        _theme = _theme == "dark" ? "light" : "dark";
        Changed?.Invoke();
    }
}
