using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Graphics;

/// <summary>
/// A sketch pad on Pointer Events. Strokes are kept as data — points with pressure — rather than
/// only as pixels, so undo and redo are a matter of redrawing the list, and the exported PNG is
/// the same drawing at full resolution.
/// </summary>
public sealed class DrawingPad : Demo
{
    public override string Id => "drawing-pad";
    public override string Title => "Drawing pad";
    public override string Category => "Graphics";
    public override string Summary => "Pointer Events with pen pressure, a stroke list for undo and redo, and PNG export through canvas.toDataURL. A mouse draws at an even weight; a pen thins and thickens as you press.";
    public override string[] Apis => new[] { "Pointer Events", "PointerEvent.pressure", "setPointerCapture", "canvas.toDataURL" };
    public override string Module => "Demos/Graphics/DrawingPad";
    public override string Hint => "Draw, then press Ctrl+Z to undo.";

    private sealed class Point
    {
        public double X;
        public double Y;
        public double Pressure;
    }

    private sealed class Stroke
    {
        public string Colour = "";
        public double Size;
        public List<Point> Points = new();
    }

    private static readonly string[] Palette = { "#eeeaf8", "#a78bfa", "#f0abfc", "#fbbf24", "#fb923c", "#6ee7b7", "#67e8f9", "#fb7185" };

    private readonly List<Stroke> _strokes = new();
    private readonly List<Stroke> _undone = new();
    private Stroke? _current;
    private HTMLCanvasElement _canvas = null!;
    private CanvasRenderingContext2D _ctx = null!;
    private ResizeObserver? _resize;
    private HTMLElement _hint = null!;
    private HTMLElement _pressure = null!;
    private HTMLElement _count = null!;
    private HTMLButtonElement _undo = null!;
    private HTMLButtonElement _redo = null!;
    private string _colour = Palette[1];
    private double _size = 8;
    private Action<KeyboardEvent> _keys = null!;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-canvas");
        var surface = stage.Add(Ui.El("div", "pad-surface"));
        _canvas = surface.Add(Dom.Create<HTMLCanvasElement>("canvas"));
        _ctx = _canvas.GetContext2D();
        _hint = surface.Add(Ui.El("p", "pad-hint", "Draw anywhere. A pen's pressure changes the line's weight."));

        var controls = stage.Add(Ui.El("div", "stage-controls pad-controls"));
        var swatches = controls.Add(Ui.El("div", "pad-swatches"));
        swatches.SetAttribute("role", "radiogroup");
        swatches.SetAttribute("aria-label", "Ink colour");
        foreach (var colour in Palette)
        {
            var swatch = swatches.Add(Ui.Button("", () => { }, "pad-swatch"));
            swatch.Style.SetProperty("--ink", colour);
            swatch.SetAttribute("aria-label", colour);
            swatch.ClassList.Toggle("is-on", colour == _colour);
            swatch.On<MouseEvent>("click", _ =>
            {
                _colour = colour;
                foreach (var other in swatches.QueryAll<HTMLElement>(".pad-swatch")) other.ClassList.Remove("is-on");
                swatch.ClassList.Add("is-on");
            });
        }

        controls.Add(Controls.Slider("Brush", 2, 40, 1, _size, v => _size = v, v => $"{v:F0} px"));
        controls.Add(Ui.Readout("Pressure", out _pressure));
        controls.Add(Ui.Readout("Strokes", out _count, "0"));
        _undo = controls.Add(Ui.Button("Undo", Undo));
        _redo = controls.Add(Ui.Button("Redo", Redo));
        controls.Add(Ui.Button("Clear", Clear, "btn btn-ghost"));
        controls.Add(Ui.Button("Export PNG", Export, "btn btn-primary"));

        _canvas.On<PointerEvent>("pointerdown", Begin);
        _canvas.On<PointerEvent>("pointermove", Extend);
        _canvas.On<PointerEvent>("pointerup", End);
        _canvas.On<PointerEvent>("pointercancel", End);

        // Ctrl+Z and Ctrl+Shift+Z work anywhere on the page while the demo is open, so the
        // listener lives on window — and must be removed again in Unmount.
        _keys = e =>
        {
            if (!(e.CtrlKey || e.MetaKey) || e.Key.ToLower() != "z") return;
            e.PreventDefault();
            if (e.ShiftKey) Redo();
            else Undo();
        };
        Js.Window.On("keydown", _keys);

        _resize = new ResizeObserver((entries, observer) =>
        {
            var ratio = Js.DevicePixelRatio;
            var box = _canvas.GetBoundingClientRect();
            _canvas.Width = Math.Floor(box.Width * ratio);
            _canvas.Height = Math.Floor(box.Height * ratio);
            _ctx.SetTransform(ratio, 0, 0, ratio, 0, 0);
            Redraw();
        });
        _resize.Observe(_canvas);
        UpdateButtons();
    }

    public override void Unmount()
    {
        _resize?.Disconnect();
        Js.Window.Off("keydown", _keys);
    }

    private void Begin(PointerEvent e)
    {
        if (e.Button != 0) return;
        _canvas.SetPointerCapture(e.PointerId);
        _current = new Stroke { Colour = _colour, Size = _size };
        _strokes.Add(_current);
        _undone.Clear();
        _hint.ClassList.Add("is-hidden");
        Extend(e);
    }

    private void Extend(PointerEvent e)
    {
        if (_current is null) return;

        // Coalesced events carry the samples the browser merged between frames; using them keeps
        // fast strokes smooth instead of polygonal.
        var samples = e.GetCoalescedEvents();
        if (samples.Length == 0) samples = new[] { e };

        foreach (var sample in samples)
        {
            _current.Points.Add(new Point
            {
                X = sample.OffsetX,
                Y = sample.OffsetY,
                // A mouse reports 0.5 while pressed; treat that as full weight.
                Pressure = sample.PointerType == "mouse" ? 1 : Math.Max(0.1, sample.Pressure),
            });
        }

        _pressure.TextContent = e.PointerType == "mouse" ? "mouse" : $"{e.Pressure:F2} {e.PointerType}";
        Redraw();
    }

    private void End(PointerEvent e)
    {
        if (_current is null) return;
        _current = null;
        UpdateButtons();
    }

    private void Undo()
    {
        if (_strokes.Count == 0) return;
        var last = _strokes[_strokes.Count - 1];
        _strokes.RemoveAt(_strokes.Count - 1);
        _undone.Add(last);
        Redraw();
        UpdateButtons();
    }

    private void Redo()
    {
        if (_undone.Count == 0) return;
        var stroke = _undone[_undone.Count - 1];
        _undone.RemoveAt(_undone.Count - 1);
        _strokes.Add(stroke);
        Redraw();
        UpdateButtons();
    }

    private void Clear()
    {
        _strokes.Clear();
        _undone.Clear();
        _hint.ClassList.Remove("is-hidden");
        Redraw();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _undo.Disabled = _strokes.Count == 0;
        _redo.Disabled = _undone.Count == 0;
        _count.TextContent = _strokes.Count.ToString();
    }

    /// <summary>Paints the paper, then every stroke in order.</summary>
    private void Redraw()
    {
        var box = _canvas.GetBoundingClientRect();
        _ctx.FillStyle = "#141022";
        _ctx.FillRect(0, 0, box.Width, box.Height);

        // A faint dot grid, like a sketchbook page.
        _ctx.FillStyle = "rgba(167, 139, 250, 0.12)";
        for (var x = 12.0; x < box.Width; x += 24)
        for (var y = 12.0; y < box.Height; y += 24)
            _ctx.FillRect(x, y, 1.5, 1.5);

        foreach (var stroke in _strokes) Paint(stroke);
    }

    /// <summary>
    /// Draws a stroke as short segments whose width follows pressure, each curved through the
    /// midpoints of its neighbours so the line has no corners.
    /// </summary>
    private void Paint(Stroke stroke)
    {
        var points = stroke.Points;
        _ctx.StrokeStyle = stroke.Colour;
        _ctx.FillStyle = stroke.Colour;
        _ctx.LineCap = "round";
        _ctx.LineJoin = "round";

        if (points.Count == 1)
        {
            _ctx.BeginPath();
            _ctx.Arc(points[0].X, points[0].Y, stroke.Size * points[0].Pressure / 2, 0, Math.PI * 2);
            _ctx.Fill();
            return;
        }

        for (var i = 1; i < points.Count; i++)
        {
            var previous = points[i - 1];
            var point = points[i];
            var startX = i == 1 ? previous.X : (points[i - 2].X + previous.X) / 2;
            var startY = i == 1 ? previous.Y : (points[i - 2].Y + previous.Y) / 2;
            var endX = (previous.X + point.X) / 2;
            var endY = (previous.Y + point.Y) / 2;

            _ctx.LineWidth = stroke.Size * (previous.Pressure + point.Pressure) / 2;
            _ctx.BeginPath();
            _ctx.MoveTo(startX, startY);
            _ctx.QuadraticCurveTo(previous.X, previous.Y, endX, endY);
            _ctx.Stroke();
        }
    }

    /// <summary>Saves the canvas as a PNG through a temporary download link.</summary>
    private void Export()
    {
        var link = Dom.Create<HTMLAnchorElement>("a");
        link.Href = _canvas.ToDataURL("image/png");
        link.Download = $"csjs-sketch-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        link.Click();
    }
}
