using CSJS.Web;

namespace CSJS.Gallery.Client.Shell;

/// <summary>Small element builders, so a demo's markup reads as a tree rather than as plumbing.</summary>
public static class Ui
{
    /// <summary>An element with an optional class and text.</summary>
    public static HTMLElement El(string tag, string className = "", string text = "")
    {
        var element = Dom.Create<HTMLElement>(tag);
        if (className.Length > 0) element.ClassName = className;
        if (text.Length > 0) element.TextContent = text;
        return element;
    }

    /// <summary>Appends <paramref name="child"/> and returns it, so a builder can keep a handle.</summary>
    public static T Add<T>(this HTMLElement parent, T child) where T : Node
    {
        parent.AppendChild(child);
        return child;
    }

    /// <summary>A button with a label and a click handler.</summary>
    public static HTMLButtonElement Button(string label, Action onClick, string className = "btn")
    {
        var button = Dom.Create<HTMLButtonElement>("button");
        button.Type = "button";
        button.ClassName = className;
        button.TextContent = label;
        button.On<MouseEvent>("click", _ => onClick());
        return button;
    }

    /// <summary>A labelled text input.</summary>
    public static HTMLInputElement Input(string placeholder, string value = "", string type = "text")
    {
        var input = Dom.Create<HTMLInputElement>("input");
        input.Type = type;
        input.Placeholder = placeholder;
        input.Value = value;
        input.ClassName = "field";
        return input;
    }

    /// <summary>A two-line readout: a mono label above a value.</summary>
    public static HTMLElement Readout(string label, out HTMLElement value, string initial = "—")
    {
        var box = El("div", "readout");
        box.Add(El("span", "readout-label", label));
        value = box.Add(El("span", "readout-value", initial));
        return box;
    }

    /// <summary>A scrolling log the newest line lands on top of.</summary>
    public static HTMLElement Log(string className = "log") => El("ol", className);

    /// <summary>Writes a line to a log made by <see cref="Log"/>, keeping the last 60.</summary>
    public static void Write(HTMLElement log, string text, string kind = "")
    {
        var line = El("li", kind.Length > 0 ? "log-" + kind : "", text);
        log.Prepend(line);
        while (log.ChildElementCount > 60) log.LastElementChild!.Remove();
    }

    /// <summary>Escapes text for HTML.</summary>
    public static string Escape(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>A short time of day, for log lines.</summary>
    public static string Clock()
    {
        var now = DateTime.Now;
        return now.ToString("HH:mm:ss");
    }
}
