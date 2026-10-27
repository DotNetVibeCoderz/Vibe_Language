using CSJS.Gallery.Client.Shell;
using CSJS.Web;
using SignalR = CSJS.Gallery.Client.Bindings.SignalR;

namespace CSJS.Gallery.Client.Demos.Network;

/// <summary>What the hub's <c>message</c> event carries: an anonymous object, camel-cased by the server.</summary>
[JsObjectLiteral]
public class ChatLine
{
    [JsName("user")] public string User { get; set; } = "";
    [JsName("text")] public string Text { get; set; } = "";
    [JsName("at")] public string At { get; set; } = "";
}

/// <summary>
/// A chat room on an ASP.NET Core SignalR hub, through the real <c>@microsoft/signalr</c> client.
/// The C# it calls was generated from the package's own <c>.d.ts</c> by <c>cs2js add</c> — see
/// <c>Bindings/Signalr.cs</c> — so <c>HubConnectionBuilder</c> here is the library's, not a copy.
/// </summary>
public sealed class SignalRChat : Demo
{
    public override string Id => "signalr";
    public override string Title => "SignalR";
    public override string Category => "Network";
    public override string Summary => "A chat room on an ASP.NET Core hub, through the real @microsoft/signalr client — its C# bindings generated from the package's typings by cs2js add. Open a second tab to talk to yourself.";
    public override string[] Apis => new[] { "@microsoft/signalr", "WebSocket", "setInterval", "performance.now" };
    public override string Module => "Demos/Network/SignalRChat";
    public override string Hint => "Open this page in two tabs and chat between them.";

    private readonly StatusBadge _badge = new();
    private SignalR.HubConnection? _connection;
    private HTMLElement _messages = null!;
    private HTMLElement _log = null!;
    private HTMLInputElement _user = null!;
    private HTMLInputElement _text = null!;
    private HTMLButtonElement _toggle = null!;
    private HTMLButtonElement _send = null!;
    private HTMLElement _online = null!;
    private HTMLElement _latency = null!;
    private HTMLElement _count = null!;
    private HTMLElement _id = null!;
    private HTMLCanvasElement _spark = null!;
    private readonly List<double> _pings = new();
    private double _pingTimer;
    private int _received;
    private bool _mounted;

    public override void Mount(HTMLElement stage)
    {
        _mounted = true;
        stage.ClassList.Add("stage-split");

        var app = stage.Add(Ui.El("div", "panel net-panel"));
        var top = app.Add(Ui.El("div", "net-top"));
        top.Add(_badge.Element);
        top.Add(Ui.El("code", "net-endpoint", "/hubs/chat"));
        _toggle = top.Add(Ui.Button("Disconnect", () => Toggle(), "btn btn-small net-toggle"));

        var readouts = app.Add(Ui.El("div", "net-readouts"));
        readouts.Add(Ui.Readout("Online", out _online));
        var latency = readouts.Add(Ui.Readout("Round trip", out _latency));
        _spark = latency.Add(Dom.Create<HTMLCanvasElement>("canvas"));
        _spark.ClassName = "net-spark";
        _spark.Width = 120;
        _spark.Height = 26;
        readouts.Add(Ui.Readout("Messages", out _count, "0"));
        readouts.Add(Ui.Readout("Connection id", out _id));

        _messages = app.Add(Ui.El("ol", "chat"));
        _messages.Add(Ui.El("li", "chat-empty", "No messages yet. Say something — everyone on this page sees it."));

        var form = app.Add(Dom.Create<HTMLFormElement>("form"));
        form.ClassName = "row chat-compose";
        _user = form.Add(Ui.Input("Name", "guest-" + Random.Shared.Next(1000, 9999)));
        _user.ClassList.Add("field-name");
        _text = form.Add(Ui.Input("Write a message"));
        _send = form.Add(Ui.Button("Send", () => { }, "btn btn-primary"));
        _send.Type = "submit";
        form.On<SubmitEvent>("submit", e =>
        {
            e.PreventDefault();
            Send();
        });

        var side = stage.Add(Ui.El("div", "panel panel-log"));
        side.Add(Ui.El("h3", "panel-title", "Hub traffic"));
        _log = side.Add(Wire.Log());

        Connect();
    }

    public override void Unmount()
    {
        _mounted = false;
        Js.ClearInterval(_pingTimer);
        _connection?.Stop();
        _connection = null;
    }

    private async Task Connect()
    {
        // The generated builder, exactly as the TypeScript docs write it.
        var connection = new SignalR.HubConnectionBuilder()
            .WithUrl("/hubs/chat")
            .WithAutomaticReconnect()
            .ConfigureLogging(SignalR.LogLevel.Warning)
            .Build();
        _connection = connection;

        // Hub events. `on` takes `(...args: any[]) => any`; the generator spreads that into
        // one overload per arity, so a one-parameter lambda receives the event's one argument.
        connection.On("message", (object payload) => Receive(payload.As<ChatLine>()));
        connection.On("presence", (object count) =>
        {
            _online.TextContent = $"{count}";
            Wire.Write(_log, Wire.In, "presence", $"{count} online");
        });

        connection.Onreconnecting(error =>
        {
            _badge.Set(Link.Connecting, "Reconnecting — " + Reason(error, "the connection dropped"));
            Wire.Write(_log, Wire.Note, "reconnecting", Reason(error, ""));
            SetEnabled(false);
        });
        connection.Onreconnected(id =>
        {
            _badge.Set(Link.Connected, "Connected again");
            _id.TextContent = Short(id);
            Wire.Write(_log, Wire.Note, "reconnected", id ?? "");
            SetEnabled(true);
        });
        connection.Onclose(error =>
        {
            Js.ClearInterval(_pingTimer);
            if (!_mounted) return;
            if (error is null) _badge.Set(Link.Disconnected, "Disconnected");
            else _badge.Set(Link.Error, "Closed — " + Reason(error, "unknown error"));
            Wire.Write(_log, error is null ? Wire.Note : Wire.Fault, "closed", Reason(error, ""));
            _toggle.TextContent = "Connect";
            _online.TextContent = "—";
            _id.TextContent = "—";
            SetEnabled(false);
        });

        _badge.Set(Link.Connecting, "Connecting — negotiating a transport…");
        Wire.Write(_log, Wire.Out, "negotiate", "POST /hubs/chat/negotiate");
        _toggle.TextContent = "Disconnect";
        SetEnabled(false);
        try
        {
            await connection.Start();
            if (connection != _connection) return;
            _badge.Set(Link.Connected, "Connected over WebSocket");
            _id.TextContent = Short(connection.ConnectionId);
            Wire.Write(_log, Wire.Note, "connected", connection.ConnectionId ?? "");
            SetEnabled(true);
            _pingTimer = Js.SetInterval(() => Ping(), 2000);
            Ping();
        }
        catch (Exception error)
        {
            if (connection != _connection) return;
            _badge.Set(Link.Error, "Could not connect — " + error.Message);
            Wire.Write(_log, Wire.Fault, "start failed", error.Message);
            _toggle.TextContent = "Connect";
        }
    }

    private async Task Toggle()
    {
        if (_connection is { } connection && connection.State != SignalR.HubConnectionState.Disconnected)
        {
            _connection = null;
            Js.ClearInterval(_pingTimer);
            await connection.Stop();
            _badge.Set(Link.Disconnected, "Disconnected — you closed the connection");
            Wire.Write(_log, Wire.Note, "stopped", "by you");
            _toggle.TextContent = "Connect";
            _online.TextContent = "—";
            _id.TextContent = "—";
            SetEnabled(false);
            return;
        }
        await Connect();
    }

    private async Task Send()
    {
        var text = _text.Value.Trim();
        if (text.Length == 0 || _connection is null) return;
        var user = _user.Value.Trim();
        Wire.Write(_log, Wire.Out, "invoke Send", $"(\"{user}\", \"{text}\")");
        _text.Value = "";
        try
        {
            await _connection.Invoke("Send", user, text);
        }
        catch (Exception error)
        {
            Wire.Write(_log, Wire.Fault, "Send failed", error.Message);
        }
    }

    /// <summary>The hub returns the number it was given; the difference from now is the round trip.</summary>
    private async Task Ping()
    {
        if (_connection is null || _connection.State != SignalR.HubConnectionState.Connected) return;
        try
        {
            var sent = Js.Performance.Now();
            var echoed = (await _connection.Invoke("Ping", sent)).As<double>();
            var rtt = Js.Performance.Now() - echoed;
            _latency.TextContent = $"{rtt:F1} ms";
            _pings.Add(rtt);
            if (_pings.Count > 30) _pings.RemoveAt(0);
            DrawSpark();
        }
        catch (Exception)
        {
            _latency.TextContent = "—";
        }
    }

    private void Receive(ChatLine line)
    {
        _received++;
        _count.TextContent = _received.ToString();
        Wire.Write(_log, Wire.In, "message", $"{line.User}: {line.Text}");

        _messages.Query<HTMLElement>(".chat-empty")?.Remove();
        var mine = line.User == _user.Value.Trim();
        var item = _messages.Add(Ui.El("li", mine ? "chat-line is-mine" : "chat-line"));
        var meta = item.Add(Ui.El("div", "chat-meta"));
        meta.Add(Ui.El("span", "chat-user", line.User));
        meta.Add(Ui.El("time", "chat-time", DateTime.Parse(line.At).ToLocalTime().ToString("HH:mm:ss")));
        item.Add(Ui.El("p", "chat-text", line.Text));
        while (_messages.ChildElementCount > 100) _messages.FirstElementChild!.Remove();
        _messages.ScrollTop = _messages.ScrollHeight;
    }

    private void DrawSpark()
    {
        var ctx = _spark.GetContext2D();
        ctx.ClearRect(0, 0, 120, 26);
        if (_pings.Count < 2) return;
        var max = Math.Max(_pings.Max(), 1);
        ctx.StrokeStyle = "#a78bfa";
        ctx.LineWidth = 1.5;
        ctx.BeginPath();
        for (var i = 0; i < _pings.Count; i++)
        {
            var x = i * 120.0 / 29;
            var y = 24 - _pings[i] / max * 20;
            if (i == 0) ctx.MoveTo(x, y);
            else ctx.LineTo(x, y);
        }
        ctx.Stroke();
    }

    private void SetEnabled(bool enabled)
    {
        _send.Disabled = !enabled;
        _text.Disabled = !enabled;
    }

    private static string Reason(CSJS.Browser.JsObject? error, string fallback)
        => error is null ? fallback : error.Get<string>("message") ?? fallback;

    private static string Short(string? id) => id is null ? "—" : id.Length > 10 ? id.Substring(0, 10) + "…" : id;
}
