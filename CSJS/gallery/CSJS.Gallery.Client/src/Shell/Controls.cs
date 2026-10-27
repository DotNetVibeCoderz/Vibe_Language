using CSJS.Web;

namespace CSJS.Gallery.Client.Shell;

/// <summary>
/// The controls most demos share: a labelled slider, a segmented choice, and the notice a demo
/// shows when the browser cannot do what it needs. Built on <see cref="Ui"/>, styled by the
/// "Shared demo controls" block in gallery.css.
/// </summary>
public static class Controls
{
    /// <summary>
    /// A range input with its label and current value. <paramref name="format"/> turns the value
    /// into the text beside it; <paramref name="onInput"/> runs on every movement.
    /// </summary>
    public static HTMLElement Slider(string label, double min, double max, double step, double value,
        Action<double> onInput, Func<double, string> format)
    {
        var box = Ui.El("label", "ctl");
        var head = box.Add(Ui.El("span", "ctl-head"));
        head.Add(Ui.El("span", "ctl-label", label));
        var readout = head.Add(Ui.El("span", "ctl-value", format(value)));

        var input = box.Add(Dom.Create<HTMLInputElement>("input"));
        input.Type = "range";
        input.ClassName = "ctl-range";
        input.Min = min.ToString();
        input.Max = max.ToString();
        input.Step = step.ToString();
        input.Value = value.ToString();
        input.On<Event>("input", _ =>
        {
            var current = double.Parse(input.Value);
            readout.TextContent = format(current);
            onInput(current);
        });
        return box;
    }

    /// <summary>
    /// A row of mutually exclusive buttons. The selected one carries <c>is-on</c> and
    /// <c>aria-pressed</c>.
    /// </summary>
    public static HTMLElement Segmented(string label, string[] options, string selected, Action<string> onChange)
    {
        var group = Ui.El("div", "seg");
        group.SetAttribute("role", "group");
        group.SetAttribute("aria-label", label);

        var buttons = new List<HTMLButtonElement>();
        foreach (var option in options)
        {
            var button = group.Add(Ui.Button(option, () => { }, "seg-option"));
            buttons.Add(button);
            button.On<MouseEvent>("click", _ =>
            {
                foreach (var other in buttons) Mark(other, other == button);
                onChange(option);
            });
            Mark(button, option == selected);
        }
        return group;
    }

    private static void Mark(HTMLButtonElement button, bool on)
    {
        button.ClassList.Toggle("is-on", on);
        button.SetAttribute("aria-pressed", on ? "true" : "false");
    }

    /// <summary>
    /// A designed state for when a feature is unavailable, denied, or not started yet: a title
    /// that says what happened and a line that says what to do about it.
    /// </summary>
    public static HTMLElement Notice(string title, string body, string tone = "")
    {
        var notice = Ui.El("div", tone.Length > 0 ? "notice notice-" + tone : "notice");
        notice.Add(Ui.El("p", "notice-title", title));
        notice.Add(Ui.El("p", "notice-body", body));
        return notice;
    }

    /// <summary>A label and a value on one line, for property lists.</summary>
    public static HTMLElement Fact(HTMLElement list, string label, string value)
    {
        var row = list.Add(Ui.El("div", "fact"));
        row.Add(Ui.El("dt", "fact-label", label));
        return row.Add(Ui.El("dd", "fact-value", value));
    }

    /// <summary>
    /// Feature detection: <c>member in target</c>. A missing API is <c>undefined</c> in
    /// JavaScript rather than null, so asking whether it exists is more reliable than comparing.
    /// </summary>
    public static bool Supports(object target, string member) => target.As<CSJS.Browser.JsObject>().Has(member);

    /// <summary>Bytes as the short unit people read: 1.4 MB, 820 KB.</summary>
    public static string Bytes(double bytes)
    {
        if (bytes < 1024) return $"{bytes:F0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        if (bytes < 1024.0 * 1024 * 1024) return $"{bytes / 1024 / 1024:F1} MB";
        return $"{bytes / 1024 / 1024 / 1024:F2} GB";
    }
}
