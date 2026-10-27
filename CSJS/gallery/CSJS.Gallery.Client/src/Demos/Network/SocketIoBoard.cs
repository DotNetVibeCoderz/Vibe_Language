using CSJS.Gallery.Client.Shell;
using CSJS.Web;
using IO = CSJS.Gallery.Client.Bindings.SocketIO;
using JsObject = CSJS.Browser.JsObject;

namespace CSJS.Gallery.Client.Demos.Network;

/// <summary>One whiteboard segment, in coordinates from 0 to 1 so every canvas size agrees.</summary>
[JsObjectLiteral]
public class Stroke
{
    [JsName("x0")] public double X0 { get; set; }
    [JsName("y0")] public double Y0 { get; set; }
    [JsName("x1")] public double X1 { get; set; }
    [JsName("y1")] public double Y1 { get; set; }
    [JsName("color")] public string Color { get; set; } = "";
    [JsName("width")] public double Width { get; set; }
}

/// <summary>What the server's <c>chat</c> and <c>presence</c> events carry.</summary>
[JsObjectLiteral]
public class RoomEvent
{
    [JsName("user")] public string? User { get; set; }
    [JsName("text")] public string? Text { get; set; }
    [JsName("at")] public string? At { get; set; }
    [JsName("online")] public int? Online { get; set; }
    [JsName("joined")] public string? Joined { get; set; }
    [JsName("left")] public string? Left { get; set; }
}

/// <summary>
/// A shared whiteboard and a chat over Socket.IO. The client is the real <c>socket.io-client</c>,
/// bound to C# by <c>cs2js add</c>; the server is a few hundred lines of C# speaking Engine.IO 4
/// over a WebSocket (<c>Realtime/SocketIoServer.cs</c>), since .NET has no Socket.IO server.
/// </summary>
public sealed class SocketIoBoard : Demo
{
    public override string Id => "socket-io";
    public override string Title => "Socket.IO";
    public override string Category => "Network";
    public override string Summary => "A shared whiteboard and a chat room through the real socket.io-client, bound to C# by cs2js add. Strokes are broadcast as events; a tab that joins later is sent the board so far.";
    public override string[] Apis => new[] { "socket.io-client", "WebSocket", "Canvas 2D", "Pointer Events" };
    public override string Module => "Demos/Network/SocketIoBoard";
    public override string Hint => "Draw here, and watch the same strokes appear in a second tab.";

    private static readonly string[] Inks = { "#a78bfa", "#fbbf24", "#6ee7b7", "#fb7185", "#eeeaf8" };

    private readonly StatusBadge _badge = new();
    private IO.Socket<JsObject, JsObject>? _socket;
    private HTMLCanvasElement _canvas = null!;
    private CanvasRenderingContext2D _ctx = null!;
    private ResizeObserver? _resize;
    private readonly List<Stroke> _strokes = new();
    private HTMLElement _messages = null!;
    private HTMLElement _log = null!;
    private HTMLInputElement _user = null!;
    private HTMLInputElement _text = null!;
    private HTMLButtonElement _toggle = null!;
    private HTMLElement _online = null!;
    private HTMLElement _sent = null!;
    private HTMLElement _received = null!;
    private HTMLElement _sid = null!;
    private string _ink = Inks[0];
    private bool _drawing;
    private double _lastX;
    private double _lastY;
    private int _sentCount;
    private int _receivedCount;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-split");
        stage.ClassList.Add("stage-board");

        var board = stage.Add(Ui.El("div", "panel net-panel"));
        var top = board.Add(Ui.El("div", "net-top"));
        top.Add(_badge.Element);
        top.Add(Ui.El("code", "net-endpoint", "/socket.io/ · websocket"));
        _toggle = top.Add(Ui.Button("Disconnect", () => Toggle(), "btn btn-small net-toggle"));

        _canvas = board.Add(Dom.Create<HTMLCanvasElement>("canvas"));
        _canvas.ClassName = "board";
        _ctx = _canvas.GetContext2D();

        var tools = board.Add(Ui.El("div", "board-tools"));
        foreach (var ink in Inks)
        {
            var swatch = tools.Add(Ui.Button("", () => { }, ink == _ink ? "swatch is-on" : "swatch"));
            swatch.Style.SetProperty("--ink", ink);
            swatch.SetAttribute("aria-label", "Ink " + ink);
            swatch.On<MouseEvent>("click", e =>
            {
                _ink = ink;
                foreach (var other in tools.QueryAll<HTMLElement>(".swatch")) other.ClassList.Remove("is-on");
                swatch.ClassList.Add("is-on");
            });
        }
        tools.Add(Ui.Readout("Online", out _online));
        tools.Add(Ui.Readout("Strokes sent", out _sent, "0"));
        tools.Add(Ui.Readout("Received", out _received, "0"));
        tools.Add(Ui.Readout("Socket id", out _sid));
        tools.Add(Ui.Button("Clear board", () => _socket?.Emit("clear"), "btn btn-small board-clear"));

        _canvas.On<PointerEvent>("pointerdown", e =>
        {
            _drawing = true;
            _lastX = e.OffsetX;
            _lastY = e.OffsetY;
            _canvas.SetPointerCapture(e.PointerId);
        });
        _canvas.On<PointerEvent>("pointermove", e =>
        {
            if (!_drawing) return;
            var box = _canvas.GetBoundingClientRect();
            var stroke = new Stroke
            {
                X0 = _lastX / box.Width,
                Y0 = _lastY / box.Height,
                X1 = e.OffsetX / box.Width,
                Y1 = e.OffsetY / box.Height,
                Color = _ink,
                Width = e.PointerType == "pen" ? 1.5 + e.Pressure * 4 : 3,
            };
            _lastX = e.OffsetX;
            _lastY = e.OffsetY;
            Draw(stroke, true);
            if (_socket is { Connected: true })
            {
                _socket.Emit("draw", stroke);
                _sentCount++;
                _sent.TextContent = _sentCount.ToString();
            }
        });
        _canvas.On<PointerEvent>("pointerup", e => _drawing = false);
        _canvas.On<PointerEvent>("pointercancel", e => _drawing = false);

        _resize = new ResizeObserver((entries, observer) => Fit());
        _resize.Observe(_canvas);

        var side = stage.Add(Ui.El("div", "panel panel-log net-side"));
        side.Add(Ui.El("h3", "panel-title", "Room"));
        _messages = side.Add(Ui.El("ol", "chat chat-compact"));
        _messages.Add(Ui.El("li", "chat-empty", "Chat and drawing share one socket. Say hello."));
        var form = side.Add(Dom.Create<HTMLFormElement>("form"));
        form.ClassName = "row chat-compose";
        _user = form.Add(Ui.Input("Name", "guest-" + Random.Shared.Next(1000, 9999)));
        _user.ClassList.Add("field-name");
        _user.On<Event>("change", e => _socket?.Emit("hello", _user.Value.Trim()));
        _text = form.Add(Ui.Input("Message"));
        form.Add(Ui.Button("Send", () => { }, "btn btn-primary")).Type = "submit";
        form.On<SubmitEvent>("submit", e =>
        {
            e.PreventDefault();
            var text = _text.Value.Trim();
            if (text.Length == 0 || _socket is not { Connected: true }) return;
            _socket.Emit("chat", text);
            _text.Value = "";
        });

        side.Add(Ui.El("h3", "panel-title net-subtitle", "Events"));
        _log = side.Add(Wire.Log());
        _log.ClassList.Add("wire-short");

        Connect();
    }

    public override void Unmount()
    {
        _resize?.Disconnect();
        _socket?.Disconnect();
        _socket = null;
    }

    private void Connect()
    {
        _badge.Set(Link.Connecting, "Connecting — Engine.IO handshake…");
        _toggle.TextContent = "Disconnect";

        // `io()` with no URL connects to this page's origin. WebSocket only: the hand-written
        // server does not implement HTTP long-polling, and a real deployment rarely needs it.
        var socket = IO.SocketIoClient.Io(new IO.ManagerOptions
        {
            Transports = new[] { "websocket" },
            ReconnectionDelay = 1000,
            ReconnectionDelayMax = 4000,
        });
        _socket = socket;

        // Listeners are `object` in the bindings — the library types them through a mapped event
        // map C# cannot express — so each lambda states its parameter types and converts by
        // its natural delegate type.
        socket.On("connect", () =>
        {
            _badge.Set(Link.Connected, "Connected over WebSocket");
            _sid.TextContent = socket.Id ?? "—";
            Wire.Write(_log, Wire.Note, "connect", socket.Id ?? "");
            socket.Emit("hello", _user.Value.Trim());
        });
        socket.On("connect_error", (JsObject error) =>
        {
            _badge.Set(Link.Error, "Could not connect — " + (error.Get<string>("message") ?? "unknown error"));
            Wire.Write(_log, Wire.Fault, "connect_error", error.Get<string>("message") ?? "");
        });
        socket.On("disconnect", (string reason) =>
        {
            _sid.TextContent = "—";
            _online.TextContent = "—";
            // "io client disconnect" is us; anything else and the client reconnects by itself.
            if (reason == "io client disconnect") _badge.Set(Link.Disconnected, "Disconnected — you closed the socket");
            else _badge.Set(Link.Connecting, $"Reconnecting — {reason}");
            Wire.Write(_log, Wire.Note, "disconnect", reason);
        });
        socket.Io.On("reconnect_attempt", (double attempt) =>
            _badge.Set(Link.Connecting, $"Reconnecting — attempt {attempt}"));

        socket.On("history", (Stroke[] strokes) =>
        {
            _strokes.Clear();
            Repaint();
            foreach (var stroke in strokes) Draw(stroke, true);
            Wire.Write(_log, Wire.In, "history", $"{strokes.Length} strokes already on the board");
        });
        socket.On("draw", (Stroke stroke) =>
        {
            Draw(stroke, true);
            _receivedCount++;
            _received.TextContent = _receivedCount.ToString();
        });
        socket.On("clear", (string who) =>
        {
            _strokes.Clear();
            Repaint();
            Wire.Write(_log, Wire.In, "clear", $"by {who}");
        });
        socket.On("chat", (RoomEvent message) => Chat(message));
        socket.On("presence", (RoomEvent presence) =>
        {
            if (presence.Online is { } online) _online.TextContent = online.ToString();
            var change = presence.Joined is { } joined ? $"{joined} joined" : presence.Left is { } left ? $"{left} left" : "";
            Wire.Write(_log, Wire.In, "presence", $"{presence.Online} online{(change.Length > 0 ? " · " + change : "")}");
        });
    }

    private void Toggle()
    {
        if (_socket is { } socket)
        {
            _socket = null;
            socket.Disconnect();
            _toggle.TextContent = "Connect";
            return;
        }
        Connect();
    }

    private void Chat(RoomEvent message)
    {
        Wire.Write(_log, Wire.In, "chat", $"{message.User}: {message.Text}");
        _messages.Query<HTMLElement>(".chat-empty")?.Remove();
        var mine = message.User == _user.Value.Trim();
        var item = _messages.Add(Ui.El("li", mine ? "chat-line is-mine" : "chat-line"));
        var meta = item.Add(Ui.El("div", "chat-meta"));
        meta.Add(Ui.El("span", "chat-user", message.User ?? "guest"));
        meta.Add(Ui.El("time", "chat-time", Ui.Clock()));
        item.Add(Ui.El("p", "chat-text", message.Text ?? ""));
        while (_messages.ChildElementCount > 60) _messages.FirstElementChild!.Remove();
        _messages.ScrollTop = _messages.ScrollHeight;
    }

    /// <summary>Matches the backing store to the element and redraws, so strokes stay sharp.</summary>
    private void Fit()
    {
        var ratio = Js.DevicePixelRatio;
        var box = _canvas.GetBoundingClientRect();
        _canvas.Width = Math.Floor(box.Width * ratio);
        _canvas.Height = Math.Floor(box.Height * ratio);
        _ctx.SetTransform(ratio, 0, 0, ratio, 0, 0);
        Repaint();
    }

    private void Repaint()
    {
        var box = _canvas.GetBoundingClientRect();
        _ctx.FillStyle = "#0d0b14";
        _ctx.FillRect(0, 0, box.Width, box.Height);

        // A faint dot grid, so an empty board still reads as a surface.
        _ctx.FillStyle = "#211d33";
        for (var x = 12.0; x < box.Width; x += 24)
            for (var y = 12.0; y < box.Height; y += 24)
                _ctx.FillRect(x, y, 1.5, 1.5);

        foreach (var stroke in _strokes) Draw(stroke, false);
    }

    private void Draw(Stroke stroke, bool remember)
    {
        if (remember) _strokes.Add(stroke);
        var box = _canvas.GetBoundingClientRect();
        _ctx.StrokeStyle = stroke.Color;
        _ctx.LineWidth = stroke.Width;
        _ctx.LineCap = "round";
        _ctx.BeginPath();
        _ctx.MoveTo(stroke.X0 * box.Width, stroke.Y0 * box.Height);
        _ctx.LineTo(stroke.X1 * box.Width, stroke.Y1 * box.Height);
        _ctx.Stroke();
    }
}
