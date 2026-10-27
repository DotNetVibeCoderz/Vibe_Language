using System.Text.Json;
using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Network;

/// <summary>Every frame <c>/ws/echo</c> sends: a welcome, an echo of what you sent, or a heartbeat.</summary>
public class EchoFrame
{
    public string Kind { get; set; } = "";
    public string? Text { get; set; }
    public int Length { get; set; }
    public int Beat { get; set; }
    public DateTime At { get; set; }
}

/// <summary>
/// A raw <c>WebSocket</c>, nothing between the page and the frames: open it, send text, and watch
/// every frame cross in both directions — the server's welcome, its echo of each message, and a
/// heartbeat it pushes every two seconds unasked.
/// </summary>
public sealed class EchoSocket : Demo
{
    public override string Id => "websocket";
    public override string Title => "WebSocket";
    public override string Category => "Network";
    public override string Summary => "A raw WebSocket to an ASP.NET Core endpoint that echoes each message and pushes a heartbeat every two seconds. Frames are parsed with System.Text.Json and counted in both directions.";
    public override string[] Apis => new[] { "WebSocket", "CloseEvent", "TextEncoder", "System.Text.Json" };
    public override string Module => "Demos/Network/EchoSocket";
    public override string Hint => "Send a message, then disconnect and read the close code.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly TextEncoder Utf8 = new();

    private readonly StatusBadge _badge = new();
    private WebSocket? _socket;
    private HTMLElement _log = null!;
    private HTMLInputElement _input = null!;
    private HTMLButtonElement _toggle = null!;
    private HTMLButtonElement _send = null!;
    private HTMLButtonElement _burst = null!;
    private HTMLElement _state = null!;
    private HTMLElement _inCount = null!;
    private HTMLElement _outCount = null!;
    private HTMLElement _inBytes = null!;
    private HTMLElement _uptime = null!;
    private HTMLElement _pulse = null!;
    private int _received;
    private int _sent;
    private double _receivedBytes;
    private double _sentBytes;
    private double _openedAt;
    private double _clock;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-split");

        var app = stage.Add(Ui.El("div", "panel net-panel"));
        var top = app.Add(Ui.El("div", "net-top"));
        top.Add(_badge.Element);
        top.Add(Ui.El("code", "net-endpoint", SocketUrl()));
        _toggle = top.Add(Ui.Button("Connect", () => Toggle(), "btn btn-small net-toggle"));

        var grid = app.Add(Ui.El("div", "net-readouts net-readouts-grid"));
        grid.Add(Ui.Readout("readyState", out _state));
        grid.Add(Ui.Readout("Open for", out _uptime));
        var beat = grid.Add(Ui.Readout("Heartbeat", out _pulse));
        beat.ClassList.Add("ws-beat");
        grid.Add(Ui.Readout("Frames in", out _inCount, "0"));
        grid.Add(Ui.Readout("Frames out", out _outCount, "0"));
        grid.Add(Ui.Readout("Bytes in · out", out _inBytes, "0 B"));

        var form = app.Add(Dom.Create<HTMLFormElement>("form"));
        form.ClassName = "row ws-compose";
        _input = form.Add(Ui.Input("Text to send", "Hello from C#"));
        _send = form.Add(Ui.Button("Send", () => { }, "btn btn-primary"));
        _send.Type = "submit";
        _burst = form.Add(Ui.Button("Send ten", () => Burst(), "btn"));
        form.On<SubmitEvent>("submit", e =>
        {
            e.PreventDefault();
            Send(_input.Value);
        });

        app.Add(Ui.El("p", "net-note",
            "Close codes say why a socket ended: 1000 is a normal close, 1001 the page going away, 1006 a connection lost without a close frame."));

        var side = stage.Add(Ui.El("div", "panel panel-log"));
        side.Add(Ui.El("h3", "panel-title", "Frames"));
        _log = side.Add(Wire.Log());

        Render();
        _clock = Js.SetInterval(() => Tick(), 250);
        Open();
    }

    public override void Unmount()
    {
        Js.ClearInterval(_clock);
        _socket?.Close(1000, "Left the page");
        _socket = null;
    }

    private static string SocketUrl()
        => (Js.Location.Protocol == "https:" ? "wss://" : "ws://") + Js.Location.Host + "/ws/echo";

    private void Open()
    {
        var socket = new WebSocket(SocketUrl());
        _socket = socket;
        _badge.Set(Link.Connecting, "Connecting — HTTP upgrade in progress…");
        Wire.Write(_log, Wire.Note, "connecting", SocketUrl());

        socket.On<Event>("open", e =>
        {
            if (socket != _socket) return;
            _openedAt = Js.Performance.Now();
            _badge.Set(Link.Connected, "Open — frames flow both ways");
            Wire.Write(_log, Wire.Note, "open", "101 Switching Protocols");
            Render();
        });

        socket.On<MessageEvent<object>>("message", e =>
        {
            if (socket != _socket) return;
            var text = e.Data<string>();
            _received++;
            _receivedBytes += Utf8.Encode(text).Length;

            var frame = JsonSerializer.Deserialize<EchoFrame>(text, Json)!;
            switch (frame.Kind)
            {
                case "welcome":
                    Wire.Write(_log, Wire.In, "welcome", text);
                    break;
                case "echo":
                    Wire.Write(_log, Wire.In, "echo", $"\"{frame.Text}\" · {frame.Length} chars · server time {frame.At.ToLocalTime():HH:mm:ss.fff}");
                    break;
                case "heartbeat":
                    _pulse.TextContent = $"#{frame.Beat}";
                    _pulse.ClassList.Remove("is-beat");
                    // Reading offsetWidth restarts the CSS animation on the same element.
                    var reflow = _pulse.OffsetWidth;
                    _pulse.ClassList.Add("is-beat");
                    Wire.Write(_log, Wire.In, "heartbeat", $"beat {frame.Beat}");
                    break;
                default:
                    Wire.Write(_log, Wire.In, "message", text);
                    break;
            }
            Render();
        });

        // `error` carries no detail by design — browsers hide it so pages cannot probe the
        // network — and is always followed by `close`, whose code is the real explanation.
        socket.On<Event>("error", e =>
        {
            if (socket != _socket) return;
            Wire.Write(_log, Wire.Fault, "error", "the browser reports no detail; see the close code");
        });

        socket.On<CloseEvent>("close", e =>
        {
            if (socket != _socket) return;
            _socket = null;
            var why = e.Code switch
            {
                1000 => "normal closure",
                1001 => "going away",
                1006 => "connection lost, no close frame",
                1011 => "server error",
                _ => "code " + e.Code,
            };
            var reason = e.Reason.Length > 0 ? $" — \"{e.Reason}\"" : "";
            if (e.Code == 1000) _badge.Set(Link.Disconnected, $"Closed — {why}{reason}");
            else _badge.Set(Link.Error, $"Closed ({e.Code}) — {why}{reason}");
            Wire.Write(_log, e.Code == 1000 ? Wire.Note : Wire.Fault, $"close {e.Code}", why + reason + (e.WasClean ? " · clean" : " · not clean"));
            Render();
        });
        Render();
    }

    private void Toggle()
    {
        if (_socket is { } socket)
        {
            // 1000 with a reason: the server reads both, and answers with its own close frame.
            socket.Close(1000, "Closed from the page");
            _badge.Set(Link.Connecting, "Closing — waiting for the server's close frame…");
            Render();
            return;
        }
        Open();
    }

    private void Send(string text)
    {
        if (_socket is null || _socket.ReadyState != 1 || text.Length == 0) return;
        _socket.Send(text);
        _sent++;
        _sentBytes += Utf8.Encode(text).Length;
        Wire.Write(_log, Wire.Out, "send", $"\"{text}\"");
        Render();
    }

    private void Burst()
    {
        for (var i = 1; i <= 10; i++) Send($"{_input.Value} #{i}");
    }

    private void Tick()
    {
        if (_socket is not null && _socket.ReadyState == 1)
        {
            var seconds = (Js.Performance.Now() - _openedAt) / 1000;
            _uptime.TextContent = $"{seconds:F1} s";
        }
    }

    private void Render()
    {
        var state = _socket?.ReadyState ?? 3;
        string[] names = { "CONNECTING", "OPEN", "CLOSING", "CLOSED" };
        _state.TextContent = $"{state} · {names[(int)state]}";
        _inCount.TextContent = _received.ToString();
        _outCount.TextContent = _sent.ToString();
        _inBytes.TextContent = $"{Wire.Bytes(_receivedBytes)} · {Wire.Bytes(_sentBytes)}";
        var open = state == 1;
        _toggle.TextContent = _socket is null ? "Connect" : "Disconnect";
        _toggle.Disabled = state == 2;
        _send.Disabled = !open;
        _burst.Disabled = !open;
        if (!open) _uptime.TextContent = "—";
    }
}
