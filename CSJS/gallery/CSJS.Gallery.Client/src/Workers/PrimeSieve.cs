using CSJS;
using CSJS.Web;

namespace CSJS.Gallery.Client.Workers;

/// <summary>What the page asks the worker for.</summary>
[JsObjectLiteral]
public class PrimeRequest
{
    [JsName("limit")] public int Limit { get; set; }
}

/// <summary>
/// What the worker sends back: progress while it counts, then the result. Messages are copied
/// between threads by structured clone, which keeps plain objects — hence an object literal.
/// </summary>
[JsObjectLiteral]
public class PrimeMessage
{
    [JsName("kind")] public string Kind { get; set; } = "";
    [JsName("fraction")] public double Fraction { get; set; }
    [JsName("count")] public int Count { get; set; }
    [JsName("largest")] public int Largest { get; set; }
    [JsName("elapsed")] public double Elapsed { get; set; }
}

/// <summary>
/// Counts the primes up to a limit with a segmented Sieve of Eratosthenes. The same C# runs in
/// two places — in the worker, and on the main thread for comparison — so the only difference
/// between the two timings is where the work happens.
/// </summary>
public static class PrimeSieve
{
    /// <summary>One segment of the number line; small enough to stay in cache.</summary>
    private const int SegmentSize = 1 << 18;

    /// <summary>
    /// Returns the count and the largest prime at or below <paramref name="limit"/>, reporting
    /// the fraction done after every segment.
    /// </summary>
    public static PrimeMessage Count(int limit, Action<double, int>? progress)
    {
        if (limit < 2) return new PrimeMessage { Kind = "done" };

        // The base primes, up to √limit, by a plain sieve.
        var root = (int)Math.Sqrt(limit);
        var small = new Uint8Array(root + 1);
        var basePrimes = new List<int>();
        for (var i = 2; i <= root; i++)
        {
            if (small[i] != 0) continue;
            basePrimes.Add(i);
            for (var j = i * i; j <= root; j += i) small[j] = 1;
        }

        var count = 0;
        var largest = 0;
        var segment = new Uint8Array(SegmentSize);

        for (var low = 2; low <= limit; low += SegmentSize)
        {
            var high = Math.Min(low + SegmentSize - 1, limit);
            segment.Fill(0);

            // Cross off each base prime's multiples that fall in [low, high].
            foreach (var p in basePrimes)
            {
                var start = Math.Max(p * p, (low + p - 1) / p * p);
                for (var j = start; j <= high; j += p) segment[j - low] = 1;
            }

            for (var n = low; n <= high; n++)
            {
                if (segment[n - low] != 0) continue;
                count++;
                largest = n;
            }

            progress?.Invoke((double)(high - 1) / (limit - 1), count);
        }

        return new PrimeMessage { Kind = "done", Fraction = 1, Count = count, Largest = largest };
    }
}
