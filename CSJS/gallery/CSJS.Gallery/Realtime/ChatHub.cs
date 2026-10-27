using Microsoft.AspNetCore.SignalR;

namespace CSJS.Gallery.Realtime;

/// <summary>The SignalR hub behind the SignalR playground: a room everyone shares.</summary>
public sealed class ChatHub : Hub
{
    private static int _online;

    public override async Task OnConnectedAsync()
    {
        var count = Interlocked.Increment(ref _online);
        await Clients.All.SendAsync("presence", count);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var count = Interlocked.Decrement(ref _online);
        await Clients.All.SendAsync("presence", count);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Broadcasts a message to everyone, the sender included.</summary>
    public Task Send(string user, string text)
        => Clients.All.SendAsync("message", new
        {
            user = string.IsNullOrWhiteSpace(user) ? "anonymous" : user.Trim(),
            text = text.Trim(),
            at = DateTime.UtcNow,
        });

    /// <summary>Round-trip timing: the client sends its clock, the server returns it untouched.</summary>
    public double Ping(double clientTime) => clientTime;
}
