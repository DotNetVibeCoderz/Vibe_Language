using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Graphics;

/// <summary>
/// Pixel filters written as plain C# loops over a canvas's RGBA bytes. The source image is painted
/// procedurally, so nothing is downloaded; each filter reads it with <c>getImageData</c>, rewrites
/// every pixel, and writes it back with <c>putImageData</c>. A divider compares before and after.
/// </summary>
public sealed class ImageFilters : Demo
{
    public override string Id => "image-filters";
    public override string Title => "Pixel filters";
    public override string Category => "Graphics";
    public override string Summary => "Grayscale, sepia, invert, edge detection, and pixelation as C# loops over the canvas's raw RGBA bytes. Drag the divider to compare against the original.";
    public override string[] Apis => new[] { "Canvas 2D", "ImageData", "Uint8ClampedArray", "Pointer Events" };
    public override string Module => "Demos/Graphics/ImageFilters";
    public override string Hint => "Pick a filter, then drag the divider across the image.";

    private static readonly string[] Filters = { "Grayscale", "Sepia", "Invert", "Edges", "Pixelate", "Posterize" };

    private HTMLCanvasElement _canvas = null!;
    private CanvasRenderingContext2D _ctx = null!;
    private ResizeObserver? _resize;
    private ImageData? _original;
    private ImageData? _filtered;
    private string _filter = "Edges";
    private double _strength = 1;
    private double _block = 10;
    private double _brightness;
    private double _split = 0.5;
    private bool _dragging;
    private HTMLElement _timing = null!;
    private HTMLElement _size = null!;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-canvas");
        _canvas = stage.Add(Dom.Create<HTMLCanvasElement>("canvas"));
        _canvas.ClassName = "filters-canvas";
        _ctx = _canvas.GetContext2DForReading();

        var bar = stage.Add(Ui.El("div", "stage-controls filters-bar"));
        bar.Add(Controls.Segmented("Filter", Filters, _filter, choice =>
        {
            _filter = choice;
            Apply();
        }));

        var sliders = stage.Add(Ui.El("div", "stage-controls filters-sliders"));
        sliders.Add(Controls.Slider("Strength", 0, 100, 1, 100, v => { _strength = v / 100; Apply(); }, v => $"{v:F0}%"));
        sliders.Add(Controls.Slider("Brightness", -60, 60, 1, 0, v => { _brightness = v; Apply(); }, v => v > 0 ? $"+{v:F0}" : $"{v:F0}"));
        sliders.Add(Controls.Slider("Block size", 2, 40, 1, 10, v => { _block = v; Apply(); }, v => $"{v:F0} px"));
        sliders.Add(Ui.Readout("Image", out _size));
        sliders.Add(Ui.Readout("Filter time", out _timing));

        _canvas.On<PointerEvent>("pointerdown", e =>
        {
            _dragging = true;
            _canvas.SetPointerCapture(e.PointerId);
            MoveSplit(e);
        });
        _canvas.On<PointerEvent>("pointermove", e => { if (_dragging) MoveSplit(e); });
        _canvas.On<PointerEvent>("pointerup", _ => _dragging = false);

        // The canvas is painted at its displayed size, so a filter costs exactly one pass over
        // the pixels people can see.
        _resize = new ResizeObserver((entries, observer) =>
        {
            var box = _canvas.GetBoundingClientRect();
            _canvas.Width = Math.Floor(box.Width);
            _canvas.Height = Math.Floor(box.Height);
            PaintSource();
            Apply();
        });
        _resize.Observe(_canvas);
    }

    public override void Unmount() => _resize?.Disconnect();

    private void MoveSplit(PointerEvent e)
    {
        _split = Math.Clamp(e.OffsetX / _canvas.Width, 0, 1);
        Compose();
    }

    /// <summary>
    /// Paints the source: a dusk landscape of layered ridges, a sun, and stars. Rich in gradients
    /// and hard edges, which is what makes filters easy to judge.
    /// </summary>
    private void PaintSource()
    {
        var w = _canvas.Width;
        var h = _canvas.Height;

        var sky = _ctx.CreateLinearGradient(0, 0, 0, h);
        sky.AddColorStop(0, "#1b1440");
        sky.AddColorStop(0.45, "#6d3fa8");
        sky.AddColorStop(0.72, "#f0839a");
        sky.AddColorStop(1, "#fbbf24");
        _ctx.FillStyle = sky;
        _ctx.FillRect(0, 0, w, h);

        // Stars: a fixed seed keeps the picture identical across resizes.
        var random = new Random(7);
        for (var i = 0; i < 160; i++)
        {
            var x = random.NextDouble() * w;
            var y = random.NextDouble() * h * 0.5;
            _ctx.FillStyle = $"rgba(255, 255, 255, {0.25 + random.NextDouble() * 0.6:F2})";
            _ctx.FillRect(x, y, 1.6, 1.6);
        }

        var sun = _ctx.CreateRadialGradient(w * 0.68, h * 0.62, 10, w * 0.68, h * 0.62, h * 0.32);
        sun.AddColorStop(0, "#fff7d6");
        sun.AddColorStop(0.35, "#fde68a");
        sun.AddColorStop(1, "rgba(253, 230, 138, 0)");
        _ctx.FillStyle = sun;
        _ctx.BeginPath();
        _ctx.Arc(w * 0.68, h * 0.62, h * 0.32, 0, Math.PI * 2);
        _ctx.Fill();

        // Ridges, far to near: each darker, taller, and rougher than the one behind it.
        string[] colours = { "#8b5cf6", "#5b3a9e", "#3a2670", "#211845", "#120d26" };
        for (var layer = 0; layer < colours.Length; layer++)
        {
            var baseline = h * (0.55 + layer * 0.09);
            var amplitude = h * (0.05 + layer * 0.025);
            _ctx.FillStyle = colours[layer];
            _ctx.BeginPath();
            _ctx.MoveTo(0, h);
            for (var x = 0.0; x <= w + 8; x += 8)
            {
                var y = baseline
                        - Math.Sin(x * 0.004 + layer * 1.7) * amplitude
                        - Math.Sin(x * 0.013 + layer * 0.9) * amplitude * 0.45
                        - Math.Abs(Math.Sin(x * 0.041 + layer)) * amplitude * 0.25;
                _ctx.LineTo(x, y);
            }
            _ctx.LineTo(w, h);
            _ctx.ClosePath();
            _ctx.Fill();
        }

        _ctx.Font = $"700 {Math.Max(28, h * 0.11):F0}px 'Bricolage Grotesque', sans-serif";
        _ctx.FillStyle = "rgba(255, 255, 255, 0.92)";
        _ctx.FillText("C# → pixels", w * 0.06, h * 0.24);

        _original = _ctx.GetImageData(0, 0, w, h);
        _size.TextContent = $"{w:F0} × {h:F0}";
    }

    /// <summary>Runs the selected filter over a copy of the original pixels.</summary>
    private void Apply()
    {
        if (_original is null) return;

        var started = Js.Performance.Now();
        var width = (int)_original.Width;
        var height = (int)_original.Height;
        var source = _original.Data;
        var result = _ctx.CreateImageData(_original.Width, _original.Height);
        var target = result.Data;

        switch (_filter)
        {
            case "Edges": Edges(source, target, width, height); break;
            case "Pixelate": Pixelate(source, target, width, height, (int)_block); break;
            default: PerPixel(source, target, width * height); break;
        }

        // Strength blends back toward the original; brightness shifts every channel.
        for (var i = 0; i < target.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
            {
                var mixed = source[i + c] + (target[i + c] - source[i + c]) * _strength;
                target[i + c] = (int)(mixed + _brightness);
            }
            target[i + 3] = 255;
        }

        _filtered = result;
        _timing.TextContent = $"{Js.Performance.Now() - started:F1} ms";
        Compose();
    }

    /// <summary>Filters where each output pixel depends only on the same input pixel.</summary>
    private void PerPixel(Uint8ClampedArray source, Uint8ClampedArray target, int pixels)
    {
        for (var p = 0; p < pixels; p++)
        {
            var i = p * 4;
            double r = source[i], g = source[i + 1], b = source[i + 2];

            switch (_filter)
            {
                case "Grayscale":
                    // Rec. 709 luma: green carries most of what the eye reads as brightness.
                    r = g = b = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    break;

                case "Sepia":
                    var sr = 0.393 * r + 0.769 * g + 0.189 * b;
                    var sg = 0.349 * r + 0.686 * g + 0.168 * b;
                    var sb = 0.272 * r + 0.534 * g + 0.131 * b;
                    r = sr; g = sg; b = sb;
                    break;

                case "Invert":
                    r = 255 - r; g = 255 - g; b = 255 - b;
                    break;

                case "Posterize":
                    // Four levels per channel: 64 colours in all.
                    r = Math.Round(r / 85) * 85;
                    g = Math.Round(g / 85) * 85;
                    b = Math.Round(b / 85) * 85;
                    break;
            }

            target[i] = (int)r;
            target[i + 1] = (int)g;
            target[i + 2] = (int)b;
        }
    }

    /// <summary>A Sobel operator on luma: the gradient's magnitude, tinted from violet to amber.</summary>
    private static void Edges(Uint8ClampedArray source, Uint8ClampedArray target, int width, int height)
    {
        var luma = new double[width * height];
        for (var p = 0; p < luma.Length; p++)
            luma[p] = 0.2126 * source[p * 4] + 0.7152 * source[p * 4 + 1] + 0.0722 * source[p * 4 + 2];

        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var p = y * width + x;
                var gx = -luma[p - width - 1] - 2 * luma[p - 1] - luma[p + width - 1]
                         + luma[p - width + 1] + 2 * luma[p + 1] + luma[p + width + 1];
                var gy = -luma[p - width - 1] - 2 * luma[p - width] - luma[p - width + 1]
                         + luma[p + width - 1] + 2 * luma[p + width] + luma[p + width + 1];
                var magnitude = Math.Min(255, Math.Sqrt(gx * gx + gy * gy));
                var t = magnitude / 255;

                target[p * 4] = (int)(magnitude * (0.65 + 0.33 * t));
                target[p * 4 + 1] = (int)(magnitude * (0.55 + 0.2 * t));
                target[p * 4 + 2] = (int)(magnitude * (0.98 - 0.84 * t));
            }
        }
    }

    /// <summary>Averages each block of pixels and fills the block with that one colour.</summary>
    private static void Pixelate(Uint8ClampedArray source, Uint8ClampedArray target, int width, int height, int block)
    {
        for (var by = 0; by < height; by += block)
        {
            for (var bx = 0; bx < width; bx += block)
            {
                double r = 0, g = 0, b = 0;
                var count = 0;
                var maxY = Math.Min(by + block, height);
                var maxX = Math.Min(bx + block, width);

                for (var y = by; y < maxY; y++)
                for (var x = bx; x < maxX; x++)
                {
                    var i = (y * width + x) * 4;
                    r += source[i];
                    g += source[i + 1];
                    b += source[i + 2];
                    count++;
                }

                for (var y = by; y < maxY; y++)
                for (var x = bx; x < maxX; x++)
                {
                    var i = (y * width + x) * 4;
                    target[i] = (int)(r / count);
                    target[i + 1] = (int)(g / count);
                    target[i + 2] = (int)(b / count);
                }
            }
        }
    }

    /// <summary>The original on the left of the divider, the filtered image on the right.</summary>
    private void Compose()
    {
        if (_original is null || _filtered is null) return;

        var w = _canvas.Width;
        var h = _canvas.Height;
        var splitX = Math.Round(w * _split);

        _ctx.PutImageData(_original, 0, 0);
        _ctx.PutImageData(_filtered, 0, 0, splitX, 0, w - splitX, h);

        _ctx.FillStyle = "rgba(13, 11, 20, 0.85)";
        _ctx.FillRect(splitX - 1, 0, 2, h);
        _ctx.FillStyle = "#eeeaf8";
        _ctx.BeginPath();
        _ctx.Arc(splitX, h / 2, 15, 0, Math.PI * 2);
        _ctx.Fill();
        _ctx.FillStyle = "#110e1a";
        _ctx.Font = "600 12px 'JetBrains Mono', monospace";
        _ctx.TextAlign = "center";
        _ctx.TextBaseline = "middle";
        _ctx.FillText("⇆", splitX, h / 2 + 1);

        Label("Original", 14, "left");
        Label(_filter, w - 14, "right");
    }

    private void Label(string text, double x, string align)
    {
        _ctx.Font = "500 10.5px 'Martian Mono', monospace";
        _ctx.TextAlign = align;
        _ctx.TextBaseline = "top";
        var width = _ctx.MeasureText(text.ToUpper()).Width + 16;
        var left = align == "left" ? x : x - width;
        _ctx.FillStyle = "rgba(13, 11, 20, 0.72)";
        _ctx.FillRect(left, 14, width, 24);
        _ctx.FillStyle = align == "left" ? "#a78bfa" : "#fbbf24";
        _ctx.FillText(text.ToUpper(), align == "left" ? x + 8 : x - 8, 21);
    }
}
