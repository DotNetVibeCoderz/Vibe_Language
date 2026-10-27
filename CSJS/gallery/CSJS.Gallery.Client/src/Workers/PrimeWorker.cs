using CSJS.Web;

namespace CSJS.Gallery.Client.Workers;

/// <summary>
/// The worker's entry point. A worker is just a module; CSJS runs a static constructor when its
/// module is evaluated, so registering the message handler here is all the start-up there is.
/// The page loads this as <c>new Worker("js/Workers/PrimeWorker.js", { type: "module" })</c>.
/// </summary>
public static class PrimeWorker
{
    /// <summary>Progress is posted at most this often, so messages never outnumber frames.</summary>
    private const double ProgressInterval = 30;

    static PrimeWorker()
    {
        // In a worker, `self` is the worker's global scope: messages from the page arrive here,
        // and postMessage sends them back.
        Js.Self.On<MessageEvent<object>>("message", message =>
        {
            var request = message.Data<PrimeRequest>();
            var started = Js.Performance.Now();
            var lastPost = 0.0;

            var result = PrimeSieve.Count(request.Limit, (fraction, count) =>
            {
                var now = Js.Performance.Now();
                if (now - lastPost < ProgressInterval) return;
                lastPost = now;
                Js.Self.PostMessage(new PrimeMessage { Kind = "progress", Fraction = fraction, Count = count });
            });

            result.Elapsed = Js.Performance.Now() - started;
            Js.Self.PostMessage(result);
        });
    }
}
