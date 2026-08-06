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
    private double _explorerWidth = 232;
    private double _propertiesWidth = 248;
    private double _chatWidth = 400;

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
        set { _explorerWidth = Math.Clamp(value, 150, 480); Changed?.Invoke(); }
    }

    public double PropertiesWidth
    {
        get => _propertiesWidth;
        set { _propertiesWidth = Math.Clamp(value, 160, 480); Changed?.Invoke(); }
    }

    public double ChatWidth
    {
        get => _chatWidth;
        set { _chatWidth = Math.Clamp(value, 280, 720); Changed?.Invoke(); }
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

    public void ToggleTheme()
    {
        _theme = _theme == "dark" ? "light" : "dark";
        Changed?.Invoke();
    }
}
