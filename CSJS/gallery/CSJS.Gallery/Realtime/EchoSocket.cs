using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CSJS.Gallery.Realtime;

/// <summary>
/// The raw WebSocket endpoint: every frame comes back as JSON with the server's timestamp, and a
/// heartbeat arrives every two seconds whether or not the client says anything.
/// </summary>
public static class EchoSocket
{
    public static async Task AcceptAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var sendLock = new SemaphoreSlim(1, 1);

        async Task SendAsync(object payload)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            await sendLock.WaitAsync(stop.Token);
            try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, stop.Token); }
            finally { sendLock.Release(); }
        }

        var heartbeat = Task.Run(async () =>
        {
            for (var beat = 1; !stop.IsCancellationRequested && socket.State == WebSocketState.Open; beat++)
            {
                try
                {
                    await Task.Delay(2000, stop.Token);
                    await SendAsync(new { kind = "heartbeat", beat, at = DateTime.UtcNow });
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException) { break; }
            }
        });

        var buffer = new byte[16 * 1024];
        try
        {
            await SendAsync(new { kind = "welcome", at = DateTime.UtcNow });
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await SendAsync(new { kind = "echo", text, length = text.Length, at = DateTime.UtcNow });
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            stop.Cancel();
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            await heartbeat;
        }
    }
}
