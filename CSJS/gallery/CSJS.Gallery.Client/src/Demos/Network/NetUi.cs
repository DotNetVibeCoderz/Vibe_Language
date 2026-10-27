using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Network;

/// <summary>The four states every network demo shows, in the same words and colours.</summary>
public enum Link
{
    Disconnected,
    Connecting,
    Connected,
    Error,
}

/// <summary>
/// A connection badge: a dot and a phrase. The phrase is specific — "Reconnecting, attempt 2",
/// "Closed by the server (1006)" — because "error" alone tells nobody anything.
/// </summary>
public sealed class StatusBadge
{
    private readonly HTMLElement _root;
    private readonly HTMLElement _text;

    public StatusBadge()
    {
        _root = Ui.El("span", "net-badge");
        _root.SetAttribute("role", "status");
        _root.Add(Ui.El("i", "net-dot"));
        _text = _root.Add(Ui.El("span", "net-badge-text", "Disconnected"));
        State = Link.Disconnected;
    }

    public HTMLElement Element => _root;

    public Link State { get; private set; }

    public void Set(Link state, string text)
    {
        State = state;
        _root.ClassName = "net-badge is-" + state.ToString().ToLowerInvariant();
        _text.TextContent = text;
    }
}

/// <summary>A traffic log: time, a direction arrow, a short label, and the payload.</summary>
public static class Wire
{
    /// <summary>Sent by this page.</summary>
    public const string Out = "out";

    /// <summary>Received from the server.</summary>
    public const string In = "in";

    /// <summary>Something that happened to the connection itself.</summary>
    public const string Note = "note";

    public const string Fault = "fault";

    public static HTMLElement Log() => Ui.El("ol", "log wire-log");

    public static HTMLElement Write(HTMLElement log, string direction, string label, string detail = "")
    {
        var line = Ui.El("li", "wire wire-" + direction);
        line.Add(Ui.El("time", "wire-time", Ui.Clock()));
        line.Add(Ui.El("span", "wire-arrow", direction == Out ? "→" : direction == In ? "←" : direction == Fault ? "!" : "·"));
        line.Add(Ui.El("span", "wire-label", label));
        if (detail.Length > 0) line.Add(Ui.El("span", "wire-detail", detail));
        log.Prepend(line);
        while (log.ChildElementCount > 80) log.LastElementChild!.Remove();
        return line;
    }

    /// <summary>A byte count the way people read them: 812 B, 14.2 KB.</summary>
    public static string Bytes(double count)
        => count < 1024 ? $"{count:F0} B" : count < 1024 * 1024 ? $"{count / 1024:F1} KB" : $"{count / 1024 / 1024:F2} MB";
}
