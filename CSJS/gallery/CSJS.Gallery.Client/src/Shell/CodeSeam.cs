using System.Text;
using CSJS.Web;

namespace CSJS.Gallery.Client.Shell;

/// <summary>
/// The seam: a demo's C# on the left, the JavaScript CSJS emitted for it on the right, and between
/// them a column where threads run from a C# line to every JavaScript line it became.
/// <para>
/// The threads come from the module's own source map — the same file a debugger uses — so what
/// they show is exactly what the transpiler recorded, not an approximation drawn for the page.
/// Hover a line on either side to follow it across; click to keep it selected.
/// </para>
/// </summary>
public sealed class CodeSeam
{
    private readonly HTMLElement _root;
    private HTMLElement _csPane = null!;
    private HTMLElement _jsPane = null!;
    private HTMLElement _threads = null!;
    private HTMLElement _status = null!;
    private SourceMap _map = null!;

    private List<HTMLElement> _csLines = new();
    private List<HTMLElement> _jsLines = new();

    private List<int> _selectedCs = new();
    private List<int> _selectedJs = new();
    private bool _pinned;
    private int _frame;

    public CodeSeam(HTMLElement host) => _root = host;

    /// <summary>Loads <c>js/{module}.js</c> and its map, and renders both sides.</summary>
    public async Task Load(string module, string focus = "")
    {
        _root.ClassName = "seam-view is-loading";
        _root.InnerHTML = "<p class=\"seam-loading\">Reading the source map…</p>";

        var javascriptResponse = await Js.Fetch($"js/{module}.js");
        var mapResponse = await Js.Fetch($"js/{module}.js.map");
        if (!javascriptResponse.Ok || !mapResponse.Ok)
        {
            _root.ClassName = "seam-view";
            _root.InnerHTML = $"<p class=\"seam-loading\">The emitted module js/{Ui.Escape(module)}.js was not found. Build the gallery client to produce it.</p>";
            return;
        }

        var javascript = await javascriptResponse.Text();
        _map = SourceMap.Parse(await mapResponse.Text());
        Render(_map.Source, javascript, module);

        // Open on the line that best explains the demo: the focus text if given, else Mount.
        var lines = _map.Source.Replace("\r\n", "\n").Split('\n');
        var start = FindLine(lines, focus.Length > 0 ? focus : "void Mount(");
        var mapped = _map.MappedSourceLines();
        var first = mapped.FirstOrDefault(l => l > start, mapped.Count > 0 ? mapped[0] : 0);
        SelectSource(first, pin: true, scroll: true);
        // Hovering scrolls only the far side; on open, both panes start at the pinned pair.
        Reveal(_csPane, _csLines, first);
    }

    private static int FindLine(string[] lines, string text)
    {
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].Contains(text)) return i;
        return 0;
    }

    private void Render(string csharp, string javascript, string module)
    {
        _root.ClassName = "seam-view";
        _root.InnerHTML = "";

        var fileName = module.Substring(module.LastIndexOf('/') + 1);
        var head = _root.Add(Ui.El("header", "seam-head"));
        head.Add(Ui.El("span", "pane-tag pane-tag-cs", $"C#  ·  {fileName}.cs"));
        _status = head.Add(Ui.El("span", "seam-status", "Hover a line to follow it across the seam"));
        head.Add(Ui.El("span", "pane-tag pane-tag-js", $"JavaScript  ·  {fileName}.js"));

        var body = _root.Add(Ui.El("div", "seam-body"));
        _csPane = body.Add(Ui.El("div", "pane pane-cs"));
        _threads = body.Add(Ui.El("div", "seam-threads"));
        _jsPane = body.Add(Ui.El("div", "pane pane-js"));

        _csLines = RenderLines(_csPane, Highlighter.Lines(csharp.TrimEnd(), CodeLanguage.CSharp), "cs");
        _jsLines = RenderLines(_jsPane, Highlighter.Lines(javascript.TrimEnd(), CodeLanguage.JavaScript), "js");

        // Mapped lines are marked, so the reader can see which C# produced code at all.
        foreach (var line in _map.MappedSourceLines())
            if (line < _csLines.Count) _csLines[line].ClassList.Add("is-mapped");

        _csPane.On<MouseEvent>("mouseover", e => Hover(e, isSource: true));
        _jsPane.On<MouseEvent>("mouseover", e => Hover(e, isSource: false));
        _csPane.On<MouseEvent>("click", e => Click(e, isSource: true));
        _jsPane.On<MouseEvent>("click", e => Click(e, isSource: false));
        _csPane.On<Event>("scroll", _ => ScheduleDraw());
        _jsPane.On<Event>("scroll", _ => ScheduleDraw());
        Js.Window.On<Event>("resize", _ => ScheduleDraw());

        var foot = _root.Add(Ui.El("footer", "seam-foot"));
        var helpers = javascript.Split('\n').FirstOrDefault(l => l.StartsWith("import {") && l.Contains("csjs-runtime")) ?? "";
        var helperCount = helpers.Length == 0 ? 0 : helpers.Split(',').Length;
        foot.Add(Ui.El("span", "", $"{_csLines.Count} lines of C#"));
        foot.Add(Ui.El("span", "", $"{_jsLines.Count} lines of JavaScript"));
        foot.Add(Ui.El("span", "", helperCount == 0 ? "no runtime helpers" : $"{helperCount} runtime helper{(helperCount == 1 ? "" : "s")} imported"));
        foot.Add(Ui.El("span", "", $"{_map.MappedSourceLines().Count} C# lines mapped"));
    }

    private static List<HTMLElement> RenderLines(HTMLElement pane, List<string> lines, string side)
    {
        var html = new StringBuilder("<ol class=\"code\">");
        for (var i = 0; i < lines.Count; i++)
            html.Append("<li data-line=\"").Append(i).Append("\" data-side=\"").Append(side).Append("\">")
                .Append(lines[i].Length == 0 ? " " : lines[i]).Append("</li>");
        html.Append("</ol>");
        pane.InnerHTML = html.ToString();
        return pane.QueryAll<HTMLElement>("li").ToList();
    }

    private static int LineOf(MouseEvent e)
    {
        var target = e.Target!.As<HTMLElement>();
        var item = target.Closest("li[data-line]")?.As<HTMLElement>();
        var value = item?.GetAttribute("data-line");
        return value is null ? -1 : int.Parse(value);
    }

    private void Hover(MouseEvent e, bool isSource)
    {
        if (_pinned) return;
        var line = LineOf(e);
        if (line < 0) return;
        if (isSource) SelectSource(line, pin: false, scroll: true);
        else SelectGenerated(line, pin: false, scroll: true);
    }

    private void Click(MouseEvent e, bool isSource)
    {
        var line = LineOf(e);
        if (line < 0) return;

        // Clicking the pinned line again releases it; clicking another line moves the pin.
        var alreadyPinned = _pinned && (isSource ? _selectedCs.Contains(line) : _selectedJs.Contains(line));
        if (alreadyPinned)
        {
            _pinned = false;
            _root.ClassList.Remove("is-pinned");
            return;
        }

        if (isSource) SelectSource(line, pin: true, scroll: true);
        else SelectGenerated(line, pin: true, scroll: true);
    }

    private void SelectSource(int csLine, bool pin, bool scroll)
    {
        var targets = _map.GeneratedLinesFor(csLine);
        Select(new List<int> { csLine }, targets, pin, scrollJs: scroll, scrollCs: false);
    }

    private void SelectGenerated(int jsLine, bool pin, bool scroll)
    {
        var sources = _map.SourceLinesFor(jsLine);
        Select(sources, new List<int> { jsLine }, pin, scrollJs: false, scrollCs: scroll);
    }

    private void Select(List<int> cs, List<int> js, bool pin, bool scrollJs, bool scrollCs)
    {
        foreach (var i in _selectedCs) if (i < _csLines.Count) _csLines[i].ClassList.Remove("is-selected");
        foreach (var i in _selectedJs) if (i < _jsLines.Count) _jsLines[i].ClassList.Remove("is-selected");

        _selectedCs = cs;
        _selectedJs = js;
        _pinned = pin;
        _root.ClassList.Toggle("is-pinned", pin);

        foreach (var i in cs) if (i < _csLines.Count) _csLines[i].ClassList.Add("is-selected");
        foreach (var i in js) if (i < _jsLines.Count) _jsLines[i].ClassList.Add("is-selected");

        if (scrollJs && js.Count > 0) Reveal(_jsPane, _jsLines, js[0]);
        if (scrollCs && cs.Count > 0) Reveal(_csPane, _csLines, cs[0]);

        _status.TextContent = cs.Count == 0 || js.Count == 0
            ? "No JavaScript was emitted for this line"
            : $"C# line {cs[0] + 1} → JavaScript line{(js.Count > 1 ? "s" : "")} {string.Join(", ", js.Select(l => l + 1))}" + (pin ? "  ·  pinned" : "");

        ScheduleDraw();
    }

    /// <summary>Scrolls a pane only as far as needed to bring a line into view, smoothly.</summary>
    private static void Reveal(HTMLElement pane, List<HTMLElement> lines, int index)
    {
        if (index >= lines.Count) return;
        var line = lines[index];
        // OffsetTop counts from the nearest positioned ancestor, not the pane; measure against
        // the pane itself so the line lands where it is meant to.
        var top = line.GetBoundingClientRect().Top - pane.GetBoundingClientRect().Top + pane.ScrollTop;
        var visibleTop = pane.ScrollTop;
        var visibleBottom = visibleTop + pane.ClientHeight;
        if (top >= visibleTop + 24 && top + line.OffsetHeight <= visibleBottom - 24) return;

        pane.ScrollTo(new ScrollToOptions { Top = Math.Max(0, top - pane.ClientHeight / 3), Behavior = "smooth" });
    }

    private void ScheduleDraw()
    {
        if (_frame != 0) return;
        _frame = (int)Js.RequestAnimationFrame(_ =>
        {
            _frame = 0;
            Draw();
        });
    }

    /// <summary>Draws one thread per (C# line, JavaScript line) pair, clipped to what is visible.</summary>
    private void Draw()
    {
        var seam = _threads.GetBoundingClientRect();
        var csBox = _csPane.GetBoundingClientRect();
        var jsBox = _jsPane.GetBoundingClientRect();
        var width = seam.Width;
        var height = seam.Height;

        var paths = new StringBuilder();
        foreach (var cs in _selectedCs)
        {
            if (cs >= _csLines.Count) continue;
            var from = _csLines[cs].GetBoundingClientRect();
            var y1 = Math.Clamp(from.Top + from.Height / 2, csBox.Top, csBox.Bottom) - seam.Top;

            foreach (var js in _selectedJs)
            {
                if (js >= _jsLines.Count) continue;
                var to = _jsLines[js].GetBoundingClientRect();
                var y2 = Math.Clamp(to.Top + to.Height / 2, jsBox.Top, jsBox.Bottom) - seam.Top;
                var bend = width * 0.55;
                paths.Append($"<path d=\"M0 {y1:F1} C {bend:F1} {y1:F1}, {width - bend:F1} {y2:F1}, {width:F1} {y2:F1}\"/>");
                paths.Append($"<circle class=\"knot-cs\" cx=\"2\" cy=\"{y1:F1}\" r=\"3\"/><circle class=\"knot-js\" cx=\"{width - 2:F1}\" cy=\"{y2:F1}\" r=\"3\"/>");
            }
        }

        _threads.InnerHTML =
            $"<svg width=\"{width:F0}\" height=\"{height:F0}\" viewBox=\"0 0 {width:F1} {height:F1}\" aria-hidden=\"true\">" +
            "<defs><linearGradient id=\"thread\" x1=\"0\" x2=\"1\" y1=\"0\" y2=\"0\">" +
            "<stop offset=\"0\" stop-color=\"var(--source)\"/><stop offset=\"1\" stop-color=\"var(--output)\"/>" +
            "</linearGradient></defs>" + paths + "</svg>";
    }
}
