using System.Text.Json;
using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Network;

/// <summary>One <c>tick</c> event's data.</summary>
public class PriceTick
{
    public string Symbol { get; set; } = "";
    public double Price { get; set; }
    public int Seq { get; set; }
    public DateTime At { get; set; }
    public bool Replay { get; set; }
}

/// <summary>
/// A live price chart fed by Server-Sent Events. The server ends the stream every thirty ticks on
/// purpose; <c>EventSource</c> reconnects by itself and sends <c>Last-Event-ID</c>, and the server
/// replays what was missed — so the line never has a gap. Pausing closes the stream for real, and
/// resuming asks for everything since the last id.
/// </summary>
public sealed class SseTicker : Demo
{
    public override string Id => "server-sent-events";
    public override string Title => "Server-Sent Events";
    public override string Category => "Network";
    public override string Summary => "A price chart drawn on a canvas from an EventSource stream. The server drops the stream every thirty ticks; the browser reconnects with Last-Event-ID and the missed ticks are replayed, so the line never breaks.";
    public override string[] Apis => new[] { "EventSource", "MessageEvent.lastEventId", "Canvas 2D", "requestAnimationFrame" };
    public override string Module => "Demos/Network/SseTicker";
    public override string Hint => "Pause for a few seconds, then resume — the gap fills in.";

    private const int Window = 90;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly StatusBadge _badge = new();
    private readonly List<PriceTick> _ticks = new();
    private EventSource? _source;
    private HTMLCanvasElement _canvas = null!;
    private CanvasRenderingContext2D _ctx = null!;
    private ResizeObserver? _resize;
    private HTMLElement _log = null!;
    private HTMLElement _price = null!;
    private HTMLElement _change = null!;
    private HTMLElement _count = null!;
    private HTMLElement _lastId = null!;
    private HTMLElement _reconnects = null!;
    private HTMLButtonElement _pause = null!;
    private int _received;
    private int _reconnectCount;
    private int _replayed;
    private string _lastEventId = "";
    private bool _paused;
    private bool _wasOpen;
    private double _frame;
    private double _arrived;
    private bool _running;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-split");
        stage.ClassList.Add("stage-wide");

        var app = stage.Add(Ui.El("div", "panel net-panel"));
        var top = app.Add(Ui.El("div", "net-top"));
        top.Add(_badge.Element);
        top.Add(Ui.El("code", "net-endpoint", "GET /sse/ticker · text/event-stream"));
        _pause = top.Add(Ui.Button("Pause", () => TogglePause(), "btn btn-small net-toggle"));

        var quote = app.Add(Ui.El("div", "sse-quote"));
        quote.Add(Ui.El("span", "sse-symbol", "CSJS"));
        _price = quote.Add(Ui.El("span", "sse-price", "—"));
        _change = quote.Add(Ui.El("span", "sse-change", ""));

        _canvas = app.Add(Dom.Create<HTMLCanvasElement>("canvas"));
        _canvas.ClassName = "sse-chart";
        _ctx = _canvas.GetContext2D();
        _resize = new ResizeObserver((entries, observer) =>
        {
            var ratio = Js.DevicePixelRatio;
            var box = _canvas.GetBoundingClientRect();
            _canvas.Width = Math.Floor(box.Width * ratio);
            _canvas.Height = Math.Floor(box.Height * ratio);
            _ctx.SetTransform(ratio, 0, 0, ratio, 0, 0);
        });
        _resize.Observe(_canvas);

        var readouts = app.Add(Ui.El("div", "net-readouts"));
        readouts.Add(Ui.Readout("Events", out _count, "0"));
        readouts.Add(Ui.Readout("Last event id", out _lastId));
        readouts.Add(Ui.Readout("Reconnects", out _reconnects, "0"));

        var side = stage.Add(Ui.El("div", "panel panel-log"));
        side.Add(Ui.El("h3", "panel-title", "Stream"));
        _log = side.Add(Wire.Log());

        Open();
        _running = true;
        _frame = Js.RequestAnimationFrame(stamp => Draw(stamp));
    }

    public override void Unmount()
    {
        _running = false;
        Js.CancelAnimationFrame(_frame);
        _resize?.Disconnect();
        _source?.Close();
        _source = null;
    }

    private void Open()
    {
        // EventSource cannot set headers. After our own close, the last id goes in the query;
        // after the browser's own reconnects it sends the Last-Event-ID header itself.
        var url = _lastEventId.Length > 0 ? $"/sse/ticker?lastEventId={_lastEventId}" : "/sse/ticker";
        var source = new EventSource(url);
        _source = source;
        _wasOpen = false;
        _badge.Set(Link.Connecting, "Connecting…");
        Wire.Write(_log, Wire.Out, "GET", url);

        source.On<Event>("open", e =>
        {
            if (source != _source) return;
            if (_wasOpen)
            {
                _reconnectCount++;
                _reconnects.TextContent = _reconnectCount.ToString();
                Wire.Write(_log, Wire.Note, "reconnected", $"the browser sent Last-Event-ID: {_lastEventId}");
            }
            _wasOpen = true;
            _replayed = 0;
            _badge.Set(Link.Connected, "Streaming — one event a second");
        });

        source.On<MessageEvent<object>>("tick", e =>
        {
            if (source != _source) return;
            var tick = JsonSerializer.Deserialize<PriceTick>(e.Data<string>(), Json)!;
            _lastEventId = e.LastEventId;
            Add(tick);
        });

        // EventSource retries by itself. `error` with readyState CONNECTING means it is about to;
        // CLOSED means it has given up — a non-200 response or the wrong content type.
        source.On<Event>("error", e =>
        {
            if (source != _source) return;
            if (source.ReadyState == 0)
            {
                _badge.Set(Link.Connecting, $"Reconnecting — the stream ended after #{_lastEventId}; retrying in 1.5 s");
                Wire.Write(_log, Wire.Note, "stream ended", "readyState 0 · the browser will retry after `retry: 1500`");
            }
            else
            {
                _badge.Set(Link.Error, "Closed — the server refused the stream");
                Wire.Write(_log, Wire.Fault, "closed", "readyState 2 · EventSource gave up");
            }
        });
    }

    private void TogglePause()
    {
        if (_paused)
        {
            _paused = false;
            _pause.TextContent = "Pause";
            Wire.Write(_log, Wire.Note, "resume", $"asking for everything after #{_lastEventId}");
            Open();
            return;
        }

        _paused = true;
        _pause.TextContent = "Resume";
        _source?.Close();
        _source = null;
        _badge.Set(Link.Disconnected, $"Paused — stream closed after #{_lastEventId}");
        Wire.Write(_log, Wire.Note, "close()", "paused by you; the server stops when the request ends");
    }

    private void Add(PriceTick tick)
    {
        // A tick the chart already has (a reconnect may overlap) is ignored.
        if (_ticks.Count > 0 && tick.Seq <= _ticks[^1].Seq) return;

        _ticks.Add(tick);
        if (_ticks.Count > Window + 1) _ticks.RemoveAt(0);
        _received++;
        _arrived = Js.Performance.Now();

        if (tick.Replay)
        {
            _replayed++;
            if (_replayed == 1) Wire.Write(_log, Wire.In, "replay", $"missed ticks from #{tick.Seq} arriving at once");
        }
        else
        {
            Wire.Write(_log, Wire.In, $"tick #{tick.Seq}", $"{tick.Price:F2}");
        }

        _count.TextContent = _received.ToString();
        _lastId.TextContent = "#" + _lastEventId;
        _price.TextContent = tick.Price.ToString("F2");
        var first = _ticks[0].Price;
        var change = (tick.Price - first) / first * 100;
        _change.TextContent = $"{(change >= 0 ? "▲" : "▼")} {Math.Abs(change):F2}% over {_ticks.Count} s";
        _change.ClassName = change >= 0 ? "sse-change is-up" : "sse-change is-down";
    }

    private void Draw(double stamp)
    {
        if (!_running) return;
        _frame = Js.RequestAnimationFrame(stamp => Draw(stamp));

        var box = _canvas.GetBoundingClientRect();
        var width = box.Width;
        var height = box.Height;
        _ctx.ClearRect(0, 0, width, height);
        if (_ticks.Count < 2 || width == 0) return;

        var left = 8.0;
        var right = width - 64;
        var topY = 18.0;
        var bottom = height - 26;

        var min = _ticks.Min(t => t.Price);
        var max = _ticks.Max(t => t.Price);
        var pad = Math.Max((max - min) * 0.15, 0.25);
        min -= pad;
        max += pad;

        // The newest point slides in over a second rather than jumping, so the line scrolls.
        var slide = _paused ? 1 : Math.Min((Js.Performance.Now() - _arrived) / 1000, 1);
        var step = (right - left) / Window;
        double X(int i) => right - (_ticks.Count - 1 - i - (1 - slide)) * step;
        double Y(double price) => bottom - (price - min) / (max - min) * (bottom - topY);

        // Grid lines and price labels.
        _ctx.Font = "11px 'JetBrains Mono', monospace";
        _ctx.TextBaseline = "middle";
        for (var g = 0; g <= 4; g++)
        {
            var value = min + (max - min) * g / 4;
            var y = Y(value);
            _ctx.StrokeStyle = "rgba(59, 52, 89, 0.55)";
            _ctx.LineWidth = 1;
            _ctx.BeginPath();
            _ctx.MoveTo(left, Math.Round(y) + 0.5);
            _ctx.LineTo(right, Math.Round(y) + 0.5);
            _ctx.Stroke();
            _ctx.FillStyle = "#6f698c";
            _ctx.FillText(value.ToString("F2"), right + 10, y);
        }

        // Time labels every fifteen ticks, on the ticks themselves.
        _ctx.TextBaseline = "top";
        for (var i = 0; i < _ticks.Count; i++)
        {
            if (_ticks[i].Seq % 15 != 0) continue;
            var x = X(i);
            if (x < left + 20) continue;
            _ctx.FillStyle = "#6f698c";
            _ctx.FillText(_ticks[i].At.ToLocalTime().ToString("HH:mm:ss"), x - 28, bottom + 8);
        }

        _ctx.Save();
        _ctx.BeginPath();
        _ctx.Rect(left, 0, right - left, height);
        _ctx.Clip();

        // The area under the line, then the line.
        var fill = _ctx.CreateLinearGradient(0, topY, 0, bottom);
        fill.AddColorStop(0, "rgba(251, 191, 36, 0.28)");
        fill.AddColorStop(1, "rgba(251, 191, 36, 0)");
        _ctx.BeginPath();
        _ctx.MoveTo(X(0), bottom);
        for (var i = 0; i < _ticks.Count; i++) _ctx.LineTo(X(i), Y(_ticks[i].Price));
        _ctx.LineTo(X(_ticks.Count - 1), bottom);
        _ctx.ClosePath();
        _ctx.FillStyle = fill;
        _ctx.Fill();

        _ctx.BeginPath();
        for (var i = 0; i < _ticks.Count; i++)
        {
            if (i == 0) _ctx.MoveTo(X(i), Y(_ticks[i].Price));
            else _ctx.LineTo(X(i), Y(_ticks[i].Price));
        }
        _ctx.StrokeStyle = "#fbbf24";
        _ctx.LineWidth = 2;
        _ctx.LineJoin = "round";
        _ctx.Stroke();

        // Replayed ticks are marked, so the reconnect is visible after the fact.
        _ctx.FillStyle = "#a78bfa";
        for (var i = 0; i < _ticks.Count; i++)
        {
            if (!_ticks[i].Replay) continue;
            _ctx.BeginPath();
            _ctx.Arc(X(i), Y(_ticks[i].Price), 2.5, 0, Math.PI * 2);
            _ctx.Fill();
        }
        _ctx.Restore();

        // The newest price: a dot with a soft pulse.
        var lastX = X(_ticks.Count - 1);
        var lastY = Y(_ticks[^1].Price);
        var pulse = (stamp % 1600) / 1600;
        _ctx.FillStyle = $"rgba(251, 191, 36, {0.35 * (1 - pulse):F3})";
        _ctx.BeginPath();
        _ctx.Arc(lastX, lastY, 4 + pulse * 10, 0, Math.PI * 2);
        _ctx.Fill();
        _ctx.FillStyle = "#fbbf24";
        _ctx.BeginPath();
        _ctx.Arc(lastX, lastY, 3.5, 0, Math.PI * 2);
        _ctx.Fill();
    }
}
