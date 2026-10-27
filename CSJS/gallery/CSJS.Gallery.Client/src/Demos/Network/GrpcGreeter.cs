using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Network;

/// <summary>
/// gRPC from the browser with no gRPC library: the request is encoded as protobuf and framed by
/// hand (<see cref="GrpcWeb"/>), posted with <c>fetch</c>, and the response body is read chunk by
/// chunk and cut back into frames. Every frame is shown as hex beside what it decodes to.
/// </summary>
public sealed class GrpcGreeter : Demo
{
    public override string Id => "grpc-web";
    public override string Title => "gRPC-Web";
    public override string Category => "Network";
    public override string Summary => "A unary call and a server stream against an ASP.NET Core gRPC service. The protobuf encoding and the five-byte frames are written by hand in C#, and every frame is shown as it crossed the wire.";
    public override string[] Apis => new[] { "fetch", "ReadableStream", "TextEncoder", "Uint8Array", "AbortController" };
    public override string Module => "Demos/Network/GrpcGreeter";
    public override string Hint => "Say hello, then start a countdown and watch the frames arrive.";

    private const string Service = "/csjs.gallery.Greeter/";

    // grpc-status codes, by number — https://grpc.io/docs/guides/status-codes/
    private static readonly string[] StatusNames =
    {
        "OK", "CANCELLED", "UNKNOWN", "INVALID_ARGUMENT", "DEADLINE_EXCEEDED", "NOT_FOUND", "ALREADY_EXISTS",
        "PERMISSION_DENIED", "RESOURCE_EXHAUSTED", "FAILED_PRECONDITION", "ABORTED", "OUT_OF_RANGE",
        "UNIMPLEMENTED", "INTERNAL", "UNAVAILABLE", "DATA_LOSS", "UNAUTHENTICATED",
    };

    private readonly StatusBadge _badge = new();
    private HTMLElement _frames = null!;
    private HTMLInputElement _name = null!;
    private HTMLInputElement _from = null!;
    private HTMLElement _reply = null!;
    private HTMLElement _count = null!;
    private HTMLElement _countLabel = null!;
    private HTMLElement _track = null!;
    private HTMLButtonElement _start = null!;
    private HTMLButtonElement _cancel = null!;
    private HTMLElement _calls = null!;
    private HTMLElement _frameCount = null!;
    private HTMLElement _bytesIn = null!;
    private HTMLElement _elapsed = null!;
    private AbortController? _stream;
    private int _callTotal;
    private int _frameTotal;
    private double _byteTotal;
    private bool _mounted;

    public override void Mount(HTMLElement stage)
    {
        _mounted = true;
        stage.ClassList.Add("stage-split");

        var app = stage.Add(Ui.El("div", "panel net-panel"));
        var top = app.Add(Ui.El("div", "net-top"));
        top.Add(_badge.Element);
        _badge.Set(Link.Disconnected, "Idle — no call in flight");
        top.Add(Ui.El("code", "net-endpoint", "POST " + Service + "…"));

        // Unary.
        var unary = app.Add(Ui.El("section", "net-section"));
        unary.Add(Ui.El("h3", "panel-title", "Unary · SayHello"));
        var form = unary.Add(Dom.Create<HTMLFormElement>("form"));
        form.ClassName = "row";
        _name = form.Add(Ui.Input("Your name", "Ada"));
        form.Add(Ui.Button("Say hello", () => { }, "btn btn-primary")).Type = "submit";
        form.On<SubmitEvent>("submit", e =>
        {
            e.PreventDefault();
            SayHello();
        });
        _reply = unary.Add(Ui.El("div", "grpc-reply"));
        _reply.Add(Ui.El("span", "grpc-reply-empty", "The decoded HelloReply lands here."));

        // Server streaming.
        var streaming = app.Add(Ui.El("section", "net-section"));
        streaming.Add(Ui.El("h3", "panel-title", "Server streaming · Countdown"));
        var controls = streaming.Add(Ui.El("div", "row"));
        controls.Add(Ui.El("label", "net-label", "From"));
        _from = controls.Add(Ui.Input("10", "10", "number"));
        _from.ClassList.Add("field-narrow");
        _from.Min = "1";
        _from.Max = "20";
        _start = controls.Add(Ui.Button("Start countdown", () => Countdown(), "btn btn-primary"));
        _cancel = controls.Add(Ui.Button("Cancel", () => _stream?.Abort(), "btn"));
        _cancel.Disabled = true;

        var countdown = streaming.Add(Ui.El("div", "grpc-countdown"));
        _count = countdown.Add(Ui.El("span", "grpc-count", "—"));
        _countLabel = countdown.Add(Ui.El("span", "grpc-count-label", "Each tick is its own frame, decoded the moment it arrives."));
        _track = streaming.Add(Ui.El("div", "grpc-track"));

        var readouts = app.Add(Ui.El("div", "net-readouts"));
        readouts.Add(Ui.Readout("Calls", out _calls, "0"));
        readouts.Add(Ui.Readout("Frames", out _frameCount, "0"));
        readouts.Add(Ui.Readout("Received", out _bytesIn, "0 B"));
        readouts.Add(Ui.Readout("Last call", out _elapsed));

        var side = stage.Add(Ui.El("div", "panel panel-log"));
        var head = side.Add(Ui.El("div", "net-log-head"));
        head.Add(Ui.El("h3", "panel-title", "On the wire"));
        var legend = head.Add(Ui.El("span", "hex-legend"));
        legend.Add(Ui.El("span", "hex-flag", "flag"));
        legend.Add(Ui.El("span", "hex-len", "length"));
        legend.Add(Ui.El("span", "hex-body", "message"));
        _frames = side.Add(Ui.El("ol", "frame-list"));
        _frames.Add(Ui.El("li", "frame-empty", "Frames appear here, newest first, as hex and as what they decode to."));
    }

    public override void Unmount()
    {
        _mounted = false;
        _stream?.Abort();
    }

    private async Task SayHello()
    {
        var name = _name.Value.Trim();
        var request = GreeterMessages.HelloRequest(name);
        _badge.Set(Link.Connecting, "Calling SayHello…");

        var (status, message) = await Call("SayHello", request, $"HelloRequest {{ name: \"{name}\" }}", null, frame =>
        {
            var (text, length) = GreeterMessages.HelloReply(frame.Payload);
            _reply.InnerHTML = "";
            _reply.Add(Ui.El("span", "grpc-reply-message", text));
            _reply.Add(Ui.El("span", "grpc-reply-meta", $"message · {length} characters, counted by the server"));
            return $"HelloReply {{ message: \"{text}\", length: {length} }}";
        });

        if (status == 0) _badge.Set(Link.Connected, "OK — one request, one reply");
        else _badge.Set(Link.Error, Describe(status, message));
    }

    private async Task Countdown()
    {
        var from = int.TryParse(_from.Value, out var parsed) ? Math.Clamp(parsed, 1, 20) : 10;
        _from.Value = from.ToString();
        _stream?.Abort();
        var abort = new AbortController();
        _stream = abort;
        _start.Disabled = true;
        _cancel.Disabled = false;

        _track.InnerHTML = "";
        for (var i = from; i >= 0; i--) _track.Add(Ui.El("i", "grpc-pip"));
        _count.TextContent = "…";
        _countLabel.TextContent = "Waiting for the first frame.";
        _badge.Set(Link.Connecting, "Opening the stream…");

        var received = 0;
        var (status, message) = await Call("Countdown", GreeterMessages.CountdownRequest(from), $"CountdownRequest {{ from: {from} }}", abort.Signal, frame =>
        {
            var (remaining, label) = GreeterMessages.Tick(frame.Payload);
            received++;
            _count.TextContent = label;
            _count.ClassList.Toggle("is-liftoff", remaining == 0);
            _countLabel.TextContent = $"Tick {received} of {from + 1} · remaining = {remaining}";
            if (_track.Children[received - 1] is { } pip) pip.ClassList.Add("is-on");
            _badge.Set(Link.Connected, $"Streaming — {received} of {from + 1} ticks");
            return $"Tick {{ remaining: {remaining}, label: \"{label}\" }}";
        });

        if (_stream == abort) _stream = null;
        _start.Disabled = false;
        _cancel.Disabled = true;

        if (status == 0) _badge.Set(Link.Connected, $"OK — stream complete, {received} ticks");
        else if (status == 1) _badge.Set(Link.Disconnected, $"Cancelled by you after {received} ticks");
        else _badge.Set(Link.Error, Describe(status, message));
    }

    /// <summary>
    /// One gRPC-Web call. Data frames go to <paramref name="onMessage"/> as they arrive; the call
    /// ends with the trailer frame, whose <c>grpc-status</c> is the real result — the HTTP status of
    /// a gRPC response is 200 even when the call failed.
    /// </summary>
    private async Task<(int Status, string Message)> Call(string method, Uint8Array message, string described, AbortSignal? signal, Func<GrpcFrame, string> onMessage)
    {
        var body = GrpcFrame.Wrap(message);
        ShowFrame(Wire.Out, "request · " + method, body, described);
        _callTotal++;
        _calls.TextContent = _callTotal.ToString();

        var headers = new Headers();
        headers.Set("content-type", "application/grpc-web+proto");
        headers.Set("x-grpc-web", "1");

        var started = Js.Performance.Now();
        try
        {
            var response = await Js.Fetch(Service + method, new RequestInit { Method = "POST", Headers = headers, Body = body, Signal = signal });
            if (!response.Ok)
                return (14, $"HTTP {response.Status} {response.StatusText}");

            // A call that fails before any message is sent comes back "trailers-only": the
            // status is in the HTTP headers and the body is empty.
            if (response.Headers.Get("grpc-status") is { } headerStatus)
            {
                var text = response.Headers.Get("grpc-message") ?? "";
                ShowNote($"trailers-only response · grpc-status: {headerStatus}");
                return (int.Parse(headerStatus), Hex.DecodeUriComponent(text));
            }

            var reader = response.Body!.GetReader();
            var frames = new GrpcFrameReader();
            var status = -1;
            var statusMessage = "";
            while (true)
            {
                var chunk = (await reader.Read()).As<ReadableStreamReadValueResult<Uint8Array>>();
                if (chunk.Done) break;
                _byteTotal += chunk.Value.Length;
                _bytesIn.TextContent = Wire.Bytes(_byteTotal);

                foreach (var frame in frames.Push(chunk.Value))
                {
                    if (!_mounted) return (1, "left the page");
                    if (frame.IsTrailer)
                    {
                        var trailers = frame.ReadTrailers();
                        status = trailers.TryGetValue("grpc-status", out var code) ? int.Parse(code) : 2;
                        statusMessage = trailers.TryGetValue("grpc-message", out var text) ? Hex.DecodeUriComponent(text) : "";
                        var summary = string.Join("  ", trailers.Select(t => $"{t.Key}: {t.Value}"));
                        ShowFrame(Wire.In, "trailers", frame.Raw, summary);
                    }
                    else
                    {
                        ShowFrame(Wire.In, "data", frame.Raw, onMessage(frame));
                    }
                }
            }

            _elapsed.TextContent = $"{Js.Performance.Now() - started:F0} ms";
            if (frames.Pending > 0) return (15, $"the stream ended inside a frame, {frames.Pending} bytes short");
            return status < 0 ? (2, "the response ended without a trailer frame") : (status, statusMessage);
        }
        catch (Exception error)
        {
            _elapsed.TextContent = $"{Js.Performance.Now() - started:F0} ms";
            if (signal is not null && signal.Aborted)
            {
                ShowNote("cancelled · the fetch was aborted, which resets the HTTP stream");
                return (1, "cancelled");
            }
            return (14, error.Message);
        }
    }

    private static string Describe(int status, string message)
    {
        var name = status >= 0 && status < StatusNames.Length ? StatusNames[status] : "UNKNOWN";
        return message.Length > 0 ? $"{name} ({status}) — {message}" : $"{name} ({status})";
    }

    /// <summary>
    /// One frame in the wire view: its header bytes and payload in separate colours, and the
    /// message they decode to underneath.
    /// </summary>
    private void ShowFrame(string direction, string label, Uint8Array raw, string decoded)
    {
        _frames.Query<HTMLElement>(".frame-empty")?.Remove();
        _frameTotal++;
        _frameCount.TextContent = _frameTotal.ToString();

        var item = Ui.El("li", "frame frame-" + direction);
        var head = item.Add(Ui.El("div", "frame-head"));
        head.Add(Ui.El("span", "wire-arrow", direction == Wire.Out ? "→" : "←"));
        head.Add(Ui.El("span", "frame-kind", label));
        head.Add(Ui.El("span", "frame-size", $"{raw.Length} bytes · 5 + {raw.Length - 5}"));
        head.Add(Ui.El("time", "wire-time", Ui.Clock()));

        var hex = item.Add(Ui.El("code", "hex"));
        hex.Add(Ui.El("span", "hex-flag", Hex.Of(raw, 0, 1)));
        hex.Add(Ui.El("span", "hex-len", Hex.Of(raw, 1, 5)));
        // Long payloads are cut, so one frame never fills the panel.
        var end = Math.Min(raw.Length, 5 + 48);
        if (raw.Length > 5) hex.Add(Ui.El("span", "hex-body", Hex.Of(raw, 5, end) + (end < raw.Length ? $" … +{raw.Length - end}" : "")));

        item.Add(Ui.El("div", "frame-decoded", decoded));
        _frames.Prepend(item);
        while (_frames.ChildElementCount > 40) _frames.LastElementChild!.Remove();
    }

    private void ShowNote(string text)
    {
        var item = Ui.El("li", "frame frame-note");
        item.Add(Ui.El("div", "frame-decoded", text));
        _frames.Prepend(item);
    }
}
