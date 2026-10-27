using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CSJS.Gallery.Realtime;

/// <summary>
/// A minimal Socket.IO server: Engine.IO protocol 4 over WebSocket, the default namespace, and
/// events with JSON payloads — what <c>socket.io-client</c> needs with
/// <c>transports: ['websocket']</c>.
/// <para>
/// .NET has no Socket.IO server of its own, and the wire format is small enough to show whole:
/// an Engine.IO packet is a one-digit type followed by data (<c>0</c> open, <c>2</c> ping,
/// <c>3</c> pong, <c>4</c> message), and a Socket.IO packet rides inside a message
/// (<c>40</c> connect, <c>41</c> disconnect, <c>42["event", data]</c> event).
/// </para>
/// </summary>
public sealed class SocketIoServer
{
    private const int PingIntervalMs = 25_000;
    private const int PingTimeoutMs = 20_000;

    private readonly ConcurrentDictionary<string, Client> _clients = new();

    private sealed class Client(WebSocket socket, string sid)
    {
        public WebSocket Socket { get; } = socket;
        public string Sid { get; } = sid;
        public string Name { get; set; } = "guest";
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }

    public async Task AcceptAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest || context.Request.Query["EIO"] != "4")
        {
            // Only the WebSocket transport is implemented; polling clients get a clear refusal.
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("This server speaks Engine.IO 4 over WebSocket only.");
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var client = new Client(socket, Guid.NewGuid().ToString("N")[..20]);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        var open = JsonSerializer.Serialize(new
        {
            sid = client.Sid,
            upgrades = Array.Empty<string>(),
            pingInterval = PingIntervalMs,
            pingTimeout = PingTimeoutMs,
            maxPayload = 1_000_000,
        });
        await SendAsync(client, "0" + open, stop.Token);

        var pinger = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(PingIntervalMs, stop.Token);
                    await SendAsync(client, "2", stop.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { break; }
            }
        });

        var buffer = new byte[64 * 1024];
        var message = new StringBuilder();
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var packet = message.ToString();
                message.Clear();
                await HandleAsync(client, packet, stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        finally
        {
            stop.Cancel();
            if (_clients.TryRemove(client.Sid, out _))
                await BroadcastAsync("presence", new { online = _clients.Count, left = client.Name }, CancellationToken.None);
            await pinger;
        }
    }

    private async Task HandleAsync(Client client, string packet, CancellationToken cancel)
    {
        if (packet.Length == 0) return;

        switch (packet[0])
        {
            case '3': // pong
                return;

            case '2': // a client-initiated ping (Engine.IO 3 habit); answer it anyway
                await SendAsync(client, "3" + packet[1..], cancel);
                return;

            case '4' when packet.StartsWith("40", StringComparison.Ordinal):
                _clients[client.Sid] = client;
                await SendAsync(client, "40" + JsonSerializer.Serialize(new { sid = client.Sid }), cancel);
                await BroadcastAsync("presence", new { online = _clients.Count, joined = client.Name }, cancel);
                return;

            case '4' when packet.StartsWith("41", StringComparison.Ordinal):
                _clients.TryRemove(client.Sid, out _);
                return;

            case '4' when packet.StartsWith("42", StringComparison.Ordinal):
                await HandleEventAsync(client, packet[2..], cancel);
                return;
        }
    }

    private async Task HandleEventAsync(Client client, string json, CancellationToken cancel)
    {
        // An optional ack id precedes the array: 42 17["event", …]. The playground does not ask
        // for acks, but a digit prefix must still be skipped to find the payload.
        var start = json.IndexOf('[');
        if (start < 0) return;

        using var document = JsonDocument.Parse(json[start..]);
        var items = document.RootElement.EnumerateArray().ToList();
        if (items.Count == 0 || items[0].ValueKind != JsonValueKind.String) return;

        var name = items[0].GetString();
        var data = items.Count > 1 ? items[1] : default;

        switch (name)
        {
            case "hello" when data.ValueKind == JsonValueKind.String:
                client.Name = data.GetString() is { Length: > 0 } n ? n[..Math.Min(n.Length, 24)] : "guest";
                await BroadcastAsync("presence", new { online = _clients.Count, joined = client.Name }, cancel);
                return;

            case "chat" when data.ValueKind == JsonValueKind.String:
                await BroadcastAsync("chat", new { user = client.Name, text = data.GetString(), at = DateTime.UtcNow }, cancel);
                return;

            case "draw" when data.ValueKind == JsonValueKind.Object:
                // Shared whiteboard strokes go to everyone but the sender, who drew them already.
                await BroadcastAsync("draw", data, cancel, except: client.Sid);
                return;
        }
    }

    private async Task BroadcastAsync(string name, object payload, CancellationToken cancel, string? except = null)
    {
        var packet = "42" + JsonSerializer.Serialize(new object[] { name, payload });
        foreach (var client in _clients.Values)
        {
            if (client.Sid == except) continue;
            try { await SendAsync(client, packet, cancel); }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException) { }
        }
    }

    private static async Task SendAsync(Client client, string packet, CancellationToken cancel)
    {
        if (client.Socket.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(packet);
        await client.SendLock.WaitAsync(cancel);
        try { await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancel); }
        finally { client.SendLock.Release(); }
    }
}
