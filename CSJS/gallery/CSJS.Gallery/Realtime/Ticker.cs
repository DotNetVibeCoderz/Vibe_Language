using System.Globalization;

namespace CSJS.Gallery.Realtime;

/// <summary>
/// The Server-Sent Events ticker. Prices come from one series every client shares — tick
/// <c>n</c> is the same number for everyone — so a client that reconnects with
/// <c>Last-Event-ID</c> can be sent exactly the ticks it missed.
/// </summary>
public static class Ticker
{
    /// <summary>Ticks already in the series when the server starts, so a first chart is not empty.</summary>
    private const int Warmup = 120;

    /// <summary>How many past ticks a fresh connection is sent before the live ones.</summary>
    private const int Backlog = 60;

    /// <summary>Live ticks per connection before the server ends the stream to show a reconnect.</summary>
    private const int LiveTicksPerStream = 30;

    private static readonly DateTime Origin = DateTime.UtcNow.AddSeconds(-Warmup);
    private static readonly List<double> Series = new() { 100.0 };
    private static readonly object Gate = new();

    /// <summary>The newest tick that exists: one per second since <see cref="Origin"/>.</summary>
    public static int Current => (int)(DateTime.UtcNow - Origin).TotalSeconds;

    /// <summary>A random walk, extended on demand. Noise hashed from the tick number keeps it repeatable.</summary>
    public static double Price(int seq)
    {
        lock (Gate)
        {
            while (Series.Count <= seq)
            {
                var n = Series.Count;
                var hashed = Math.Sin(n * 12.9898 + 78.233) * 43758.5453;
                var noise = hashed - Math.Floor(hashed) - 0.5;
                var drift = Math.Sin(n / 40.0) * 0.0015;
                Series.Add(Math.Round(Series[n - 1] * (1 + noise / 45 + drift), 2));
            }
            return Series[seq];
        }
    }

    public static async Task StreamAsync(HttpContext context, CancellationToken cancel)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        // The browser sends Last-Event-ID on its own reconnects; a page that closed the stream
        // itself (pause, then resume) cannot set headers on EventSource, so it uses the query.
        var lastId = context.Request.Headers["Last-Event-ID"].FirstOrDefault() ?? context.Request.Query["lastEventId"].FirstOrDefault();
        var current = Current;
        var resumed = int.TryParse(lastId, out var last);
        var next = resumed
            ? Math.Max(last + 1, current - 300)
            : current - Backlog + 1;

        // `retry` tells the browser how long to wait before reconnecting.
        await context.Response.WriteAsync("retry: 1500\n\n", cancel);

        var live = 0;
        try
        {
            while (!cancel.IsCancellationRequested && live < LiveTicksPerStream)
            {
                // Only a resumed stream replays; a fresh one opens with plain history.
                var replaying = resumed && next < Current;
                var first = next;
                while (next <= Current)
                {
                    var at = Origin.AddSeconds(next).ToString("O", CultureInfo.InvariantCulture);
                    var price = Price(next).ToString(CultureInfo.InvariantCulture);
                    await context.Response.WriteAsync(
                        $"id: {next}\nevent: tick\ndata: {{\"symbol\":\"CSJS\",\"price\":{price},\"seq\":{next},\"at\":\"{at}\",\"replay\":{(replaying && next < Current ? "true" : "false")}}}\n\n",
                        cancel);
                    next++;
                }
                await context.Response.Body.FlushAsync(cancel);
                if (next > first) live++;

                // Sleep until just past the next whole tick, so it exists when we wake.
                var due = Origin.AddSeconds(next) - DateTime.UtcNow + TimeSpan.FromMilliseconds(20);
                await Task.Delay(due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(20), cancel);
            }

            // A comment line, then the end of the response: the browser sees the stream close and
            // reconnects after `retry`.
            await context.Response.WriteAsync(": the server ends this stream to show a reconnect\n\n", cancel);
        }
        catch (OperationCanceledException) { }
    }
}
