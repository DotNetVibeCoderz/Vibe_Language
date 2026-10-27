using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Graphics;

/// <summary>
/// Thousands of particles drifting through a flow field, leaving trails. The field is a sum of
/// sines — cheap enough to evaluate per particle per frame — and the pointer bends it.
/// </summary>
public sealed class FlowField : Demo
{
    public override string Id => "flow-field";
    public override string Title => "Flow field";
    public override string Category => "Graphics";
    public override string Summary => "Particles following a vector field across a canvas, drawn every frame from C#. Move the pointer through it to bend the current.";
    public override string[] Apis => new[] { "Canvas 2D", "requestAnimationFrame", "ResizeObserver", "Pointer Events" };
    public override string Module => "Demos/Graphics/FlowField";
    public override string Hint => "Move the pointer across the canvas.";

    private struct Particle
    {
        public double X;
        public double Y;
        public double Age;
    }

    private HTMLCanvasElement _canvas = null!;
    private CanvasRenderingContext2D _ctx = null!;
    private ResizeObserver? _resize;
    private Particle[] _particles = Array.Empty<Particle>();
    private double _frame;
    private double _time;
    private double _pointerX = -1;
    private double _pointerY = -1;
    private bool _running;
    private bool _warm = true;
    private HTMLElement _fps = null!;
    private double _lastStamp;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-canvas");
        _canvas = stage.Add(Dom.Create<HTMLCanvasElement>("canvas"));
        _ctx = _canvas.GetContext2D();

        var controls = stage.Add(Ui.El("div", "stage-controls"));
        controls.Add(Ui.Readout("Particles", out var count));
        controls.Add(Ui.Readout("Frame rate", out _fps));
        controls.Add(Ui.Button("Swap palette", () => _warm = !_warm));
        controls.Add(Ui.Button("Clear", Clear));

        _canvas.On<PointerEvent>("pointermove", e =>
        {
            _pointerX = e.OffsetX;
            _pointerY = e.OffsetY;
        });
        _canvas.On<PointerEvent>("pointerleave", _ => _pointerX = -1);

        // The canvas backing store follows the element's size and the screen's pixel density,
        // so lines stay sharp on high-DPI displays.
        _resize = new ResizeObserver((entries, observer) =>
        {
            var ratio = Js.DevicePixelRatio;
            var box = _canvas.GetBoundingClientRect();
            _canvas.Width = Math.Floor(box.Width * ratio);
            _canvas.Height = Math.Floor(box.Height * ratio);
            _ctx.Scale(ratio, ratio);
            Seed(Math.Min(2600, (int)(box.Width * box.Height / 260)));
            count.TextContent = _particles.Length.ToString("N0");
            Clear();
        });
        _resize.Observe(_canvas);

        _running = true;
        _frame = Js.RequestAnimationFrame(Tick);
    }

    public override void Unmount()
    {
        _running = false;
        Js.CancelAnimationFrame(_frame);
        _resize?.Disconnect();
    }

    private void Seed(int count)
    {
        var box = _canvas.GetBoundingClientRect();
        _particles = new Particle[count];
        for (var i = 0; i < count; i++)
        {
            _particles[i].X = Random.Shared.NextDouble() * box.Width;
            _particles[i].Y = Random.Shared.NextDouble() * box.Height;
            _particles[i].Age = Random.Shared.NextDouble() * 200;
        }
    }

    private void Clear()
    {
        var box = _canvas.GetBoundingClientRect();
        _ctx.FillStyle = "#110e1a";
        _ctx.FillRect(0, 0, box.Width, box.Height);
    }

    /// <summary>The field's angle at a point: a few sines, drifting with time.</summary>
    private double Angle(double x, double y)
    {
        var angle = Math.Sin(x * 0.0042 + _time * 0.3) * 1.6
                  + Math.Cos(y * 0.0051 - _time * 0.22) * 1.6
                  + Math.Sin((x + y) * 0.0023) * 0.8;

        // The pointer is a vortex: near it, particles swing round it.
        if (_pointerX >= 0)
        {
            var dx = x - _pointerX;
            var dy = y - _pointerY;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 160)
                angle += (1 - distance / 160) * (Math.Atan2(dy, dx) + Math.PI / 2 - angle);
        }

        return angle;
    }

    private void Tick(double stamp)
    {
        if (!_running) return;

        var elapsed = _lastStamp == 0 ? 16 : stamp - _lastStamp;
        _lastStamp = stamp;
        _fps.TextContent = $"{1000 / Math.Max(elapsed, 1):F0} fps";
        _time += elapsed / 1000;

        var box = _canvas.GetBoundingClientRect();
        var width = box.Width;
        var height = box.Height;

        // Fading the previous frame rather than clearing it is what leaves the trails.
        _ctx.FillStyle = "rgba(17, 14, 26, 0.08)";
        _ctx.FillRect(0, 0, width, height);
        _ctx.LineWidth = 1.1;

        for (var i = 0; i < _particles.Length; i++)
        {
            var x = _particles[i].X;
            var y = _particles[i].Y;
            var angle = Angle(x, y);
            var nx = x + Math.Cos(angle) * 1.6;
            var ny = y + Math.Sin(angle) * 1.6;

            var hue = _warm ? 32 + (i % 24) : 258 + (i % 30);
            _ctx.StrokeStyle = $"hsla({hue}, 90%, {62 + (i % 7) * 3}%, 0.55)";
            _ctx.BeginPath();
            _ctx.MoveTo(x, y);
            _ctx.LineTo(nx, ny);
            _ctx.Stroke();

            _particles[i].X = nx;
            _particles[i].Y = ny;
            _particles[i].Age += 1;

            // A particle that leaves the canvas, or has lived long enough, is reborn elsewhere.
            if (nx < 0 || ny < 0 || nx > width || ny > height || _particles[i].Age > 400)
            {
                _particles[i].X = Random.Shared.NextDouble() * width;
                _particles[i].Y = Random.Shared.NextDouble() * height;
                _particles[i].Age = 0;
            }
        }

        _frame = Js.RequestAnimationFrame(Tick);
    }
}
