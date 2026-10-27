using CSJS.Gallery.Client.Shell;
using CSJS.Gallery.Client.Workers;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Performance;

/// <summary>
/// The same prime count run twice: in a Web Worker, where the page keeps animating, and on the
/// main thread, where it freezes. A frame-time chart makes the difference visible. The worker is
/// itself C#, transpiled by CSJS into the module the Worker constructor loads.
/// </summary>
public sealed class WorkerPrimes : Demo
{
    public override string Id => "web-worker";
    public override string Title => "Web Worker";
    public override string Category => "Performance";
    public override string Summary => "Count primes with a segmented sieve in a Web Worker written in C#, then run the very same code on the main thread. Watch the ball and the frame chart: one keeps moving, the other stops dead.";
    public override string[] Apis => new[] { "Worker (module)", "postMessage", "requestAnimationFrame", "performance.now" };
    public override string Module => "Demos/Performance/WorkerPrimes";
    public override string Hint => "Run it in the worker, then on the main thread, and compare.";

    private const int History = 240;
    private static readonly string[] Limits = { "10 M", "40 M", "100 M" };

    private Worker? _worker;
    private int _limit = 40_000_000;
    private double _runStarted;
    private bool _busy;

    private HTMLCanvasElement _canvas = null!;
    private CanvasRenderingContext2D _ctx = null!;
    private ResizeObserver? _resize;
    private readonly double[] _frames = new double[History];
    private int _frameIndex;
    private double _lastStamp;
    private double _ball;
    private double _frame;
    private bool _running;
    private double _longestDuringRun;

    private HTMLElement _fps = null!;
    private HTMLElement _longest = null!;
    private HTMLElement _progressFill = null!;
    private HTMLElement _progressText = null!;
    private HTMLButtonElement _workerButton = null!;
    private HTMLButtonElement _mainButton = null!;
    private HTMLElement _results = null!;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-canvas");
        _canvas = stage.Add(Dom.Create<HTMLCanvasElement>("canvas"));
        _canvas.ClassName = "worker-canvas";
        _ctx = _canvas.GetContext2D();

        var progress = stage.Add(Ui.El("div", "worker-progress"));
        var track = progress.Add(Ui.El("div", "worker-track"));
        _progressFill = track.Add(Ui.El("i"));
        _progressText = progress.Add(Ui.El("span", "worker-progress-text", "Idle"));

        var controls = stage.Add(Ui.El("div", "stage-controls"));
        controls.Add(Controls.Segmented("Count primes up to", Limits, Limits[1], choice =>
        {
            _limit = choice switch { "10 M" => 10_000_000, "100 M" => 100_000_000, _ => 40_000_000 };
        }));
        _workerButton = controls.Add(Ui.Button("Run in a worker", RunInWorker, "btn btn-primary"));
        _mainButton = controls.Add(Ui.Button("Run on the main thread", () => RunOnMainThread()));
        controls.Add(Ui.Readout("Frame rate", out _fps));
        controls.Add(Ui.Readout("Longest frame", out _longest));

        var table = stage.Add(Ui.El("div", "worker-results"));
        var head = table.Add(Ui.El("div", "worker-row worker-row-head"));
        foreach (var label in new[] { "Where", "Up to", "Primes", "Largest", "Time", "Longest frame" })
            head.Add(Ui.El("span", "", label));
        _results = table.Add(Ui.El("div", "worker-rows"));
        _results.Add(Ui.El("p", "empty", "Runs appear here. The longest frame is the one that matters: above 50 ms, people feel it."));

        StartWorker();

        _resize = new ResizeObserver((entries, observer) =>
        {
            var ratio = Js.DevicePixelRatio;
            var box = _canvas.GetBoundingClientRect();
            _canvas.Width = Math.Floor(box.Width * ratio);
            _canvas.Height = Math.Floor(box.Height * ratio);
            _ctx.SetTransform(ratio, 0, 0, ratio, 0, 0);
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
        _worker?.Terminate();
        _worker = null;
    }

    /// <summary>
    /// Loads the worker module. <c>type: "module"</c> lets it import, so the worker shares
    /// PrimeSieve with the page instead of carrying a copy.
    /// </summary>
    private void StartWorker()
    {
        _worker?.Terminate();
        _worker = new Worker("js/Workers/PrimeWorker.js", new WorkerOptions { Type = "module", Name = "primes" });
        _worker.On<MessageEvent<object>>("message", message =>
        {
            var update = message.Data<PrimeMessage>();
            if (update.Kind == "progress")
            {
                ShowProgress(update.Fraction, $"Worker: {update.Count:N0} primes so far");
                return;
            }

            _busy = false;
            ShowProgress(1, $"Worker finished in {update.Elapsed:N0} ms");
            AddResult("Worker", update, update.Elapsed);
            SetButtons();
        });
        _worker.On<ErrorEvent>("error", e =>
        {
            _busy = false;
            ShowProgress(0, $"The worker failed: {e.Message}");
            SetButtons();
        });
    }

    private void RunInWorker()
    {
        if (_busy || _worker is null) return;
        _busy = true;
        _longestDuringRun = 0;
        SetButtons();
        ShowProgress(0, "Worker: starting");
        _worker.PostMessage(new PrimeRequest { Limit = _limit });
    }

    /// <summary>
    /// Runs the sieve right here. The status is painted first — a short timeout lets the browser
    /// draw it — and then nothing paints at all until the count is done.
    /// </summary>
    private async Task RunOnMainThread()
    {
        if (_busy) return;
        _busy = true;
        _longestDuringRun = 0;
        SetButtons();
        ShowProgress(0, "Main thread: counting — the page cannot draw until it finishes");
        await Task.Delay(60);

        _runStarted = Js.Performance.Now();
        var result = PrimeSieve.Count(_limit, null);
        var elapsed = Js.Performance.Now() - _runStarted;

        // Hand back one frame so the frozen interval shows up in the chart before it is reported.
        await Task.Delay(32);
        _busy = false;
        ShowProgress(1, $"Main thread finished in {elapsed:N0} ms");
        AddResult("Main thread", result, elapsed);
        SetButtons();
    }

    private void SetButtons()
    {
        _workerButton.Disabled = _busy;
        _mainButton.Disabled = _busy;
    }

    private void ShowProgress(double fraction, string text)
    {
        _progressFill.Style.Width = $"{fraction * 100:F1}%";
        _progressText.TextContent = text;
    }

    private void AddResult(string where, PrimeMessage result, double elapsed)
    {
        if (_results.Query<HTMLElement>(".empty") is { } placeholder) placeholder.Remove();
        var row = Ui.El("div", where == "Worker" ? "worker-row is-worker" : "worker-row is-main");
        row.Add(Ui.El("span", "worker-where", where));
        row.Add(Ui.El("span", "", $"{_limit / 1_000_000} M"));
        row.Add(Ui.El("span", "", result.Count.ToString("N0")));
        row.Add(Ui.El("span", "", result.Largest.ToString("N0")));
        row.Add(Ui.El("span", "", $"{elapsed:N0} ms"));
        row.Add(Ui.El("span", _longestDuringRun > 50 ? "worker-jank" : "worker-smooth", $"{_longestDuringRun:N0} ms"));
        _results.Prepend(row);
        while (_results.ChildElementCount > 6) _results.LastElementChild!.Remove();
    }

    /// <summary>
    /// Records how long each frame took and draws the chart. A frame longer than 50 ms is a
    /// stutter people notice; one of several hundred is the page frozen.
    /// </summary>
    private void Tick(double stamp)
    {
        if (!_running) return;

        var elapsed = _lastStamp == 0 ? 16.7 : stamp - _lastStamp;
        _lastStamp = stamp;
        _frames[_frameIndex] = elapsed;
        _frameIndex = (_frameIndex + 1) % History;
        if (_busy) _longestDuringRun = Math.Max(_longestDuringRun, elapsed);

        var recent = 0.0;
        for (var i = 1; i <= 30; i++) recent += _frames[(_frameIndex - i + History) % History];
        _fps.TextContent = recent > 0 ? $"{30000 / recent:F0} fps" : "—";
        _longest.TextContent = $"{_frames.Max():F0} ms";

        Draw(elapsed);
        _frame = Js.RequestAnimationFrame(Tick);
    }

    private void Draw(double elapsed)
    {
        var box = _canvas.GetBoundingClientRect();
        var w = box.Width;
        var h = box.Height;
        _ctx.FillStyle = "#0d0b14";
        _ctx.FillRect(0, 0, w, h);

        // The ball: it advances with real time, so a frozen thread shows as a jump.
        _ball += elapsed / 1000 * 0.45;
        var laneY = 54.0;
        _ctx.StrokeStyle = "rgba(167, 139, 250, 0.15)";
        _ctx.LineWidth = 2;
        _ctx.BeginPath();
        _ctx.MoveTo(28, laneY);
        _ctx.LineTo(w - 28, laneY);
        _ctx.Stroke();
        var t = _ball % 2;
        var x = 28 + (t < 1 ? t : 2 - t) * (w - 56);
        _ctx.FillStyle = "#a78bfa";
        _ctx.ShadowColor = "rgba(167, 139, 250, 0.8)";
        _ctx.ShadowBlur = 16;
        _ctx.BeginPath();
        _ctx.Arc(x, laneY, 11, 0, Math.PI * 2);
        _ctx.Fill();
        _ctx.ShadowBlur = 0;

        // Frame times, newest on the right, against the 16.7 ms and 50 ms lines.
        var chartTop = 100.0;
        var chartHeight = h - chartTop - 22;
        var scale = chartHeight / 200;
        var barWidth = (w - 56) / History;

        foreach (var (limit, label) in new[] { (16.7, "16.7 ms · 60 fps"), (50.0, "50 ms · noticeable") })
        {
            var y = chartTop + chartHeight - limit * scale;
            _ctx.StrokeStyle = limit < 20 ? "rgba(110, 231, 183, 0.3)" : "rgba(251, 113, 133, 0.35)";
            _ctx.LineWidth = 1;
            _ctx.SetLineDash(new[] { 4.0, 4 });
            _ctx.BeginPath();
            _ctx.MoveTo(28, y);
            _ctx.LineTo(w - 28, y);
            _ctx.Stroke();
            _ctx.SetLineDash(Array.Empty<double>());
            _ctx.FillStyle = "rgba(163, 157, 189, 0.6)";
            _ctx.Font = "10px 'JetBrains Mono', monospace";
            _ctx.FillText(label, w - 160, y - 5);
        }

        for (var i = 0; i < History; i++)
        {
            var value = _frames[(_frameIndex + i) % History];
            if (value <= 0) continue;
            var height = Math.Min(value, 200) * scale;
            _ctx.FillStyle = value > 50 ? "#fb7185" : value > 20 ? "#fbbf24" : "rgba(167, 139, 250, 0.7)";
            _ctx.FillRect(28 + i * barWidth, chartTop + chartHeight - height, Math.Max(1, barWidth - 1), height);
        }

        _ctx.FillStyle = "rgba(111, 105, 140, 0.9)";
        _ctx.Font = "500 9.5px 'Martian Mono', monospace";
        _ctx.FillText("FRAME TIME · LAST 240 FRAMES", 28, chartTop - 10);
    }
}
