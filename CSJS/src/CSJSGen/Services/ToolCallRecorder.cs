using System.Diagnostics;
using CSJSGen.Models;
using Microsoft.SemanticKernel;

namespace CSJSGen.Services;

/// <summary>
/// Records every kernel function Jack invokes into the reply being written, so the transcript
/// shows the compile, run, and edit steps rather than leaving them implied.
/// <para>
/// A function-invocation filter rather than an auto-invocation one: every connector routes tool
/// calls through <see cref="KernelFunction.InvokeAsync(Kernel, KernelArguments?, CancellationToken)"/>,
/// including Gemini's older tool-call behaviour, so this sees them all. It observes and never
/// alters — the function's result and any exception reach the model exactly as before.
/// </para>
/// </summary>
public sealed class ToolCallRecorder : IFunctionInvocationFilter
{
    private readonly Func<ChatMessage?> _target;
    private readonly Action _changed;

    /// <summary>Longest argument value kept. Enough to recognise; the editor has the rest.</summary>
    public const int MaxArgumentLength = 1200;

    /// <summary>Longest result kept. Compile output can be the whole emitted module.</summary>
    public const int MaxResultLength = 4000;

    /// <param name="target">The message calls are recorded into; null records nothing.</param>
    /// <param name="changed">Raised when a call starts and when it ends.</param>
    public ToolCallRecorder(Func<ChatMessage?> target, Action changed)
    {
        _target = target;
        _changed = changed;
    }

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var message = _target();
        if (message is null)
        {
            await next(context);
            return;
        }

        var call = new ToolCall
        {
            Plugin = context.Function.PluginName ?? "",
            Function = context.Function.Name,
            Arguments = context.Arguments
                .Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => Truncate(Render(pair.Value), MaxArgumentLength)),
            Offset = message.Content.Length,
        };

        lock (message.ToolCalls) message.ToolCalls.Add(call);
        _changed();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);

            var result = Render(context.Result.GetValue<object>());
            call.Result = Truncate(result, MaxResultLength);
            call.Outcome = Classify(result);
        }
        catch (Exception ex)
        {
            call.Result = Truncate(ex.Message, MaxResultLength);
            call.Outcome = ToolCallOutcome.Error;
            throw;
        }
        finally
        {
            call.DurationMs = stopwatch.Elapsed.TotalMilliseconds;

            // A cancelled call never set an outcome; it did not succeed, so it does not say so.
            if (call.Outcome == ToolCallOutcome.Running) call.Outcome = ToolCallOutcome.Error;
            _changed();
        }
    }

    /// <summary>
    /// Distinguishes a function that ran and found a problem from one that ran clean. The
    /// plugins report verdicts as leading markers — <c>COMPILE FAILED</c>, <c>RUN FAILED</c>,
    /// <c>NOT SUPPORTED</c> — or, for the editing functions, a trailing "does NOT compile".
    /// </summary>
    public static ToolCallOutcome Classify(string result)
    {
        var text = result.TrimStart();

        if (text.StartsWith("COMPILE FAILED", StringComparison.Ordinal)
            || text.StartsWith("RUN FAILED", StringComparison.Ordinal)
            || text.StartsWith("NOT SUPPORTED", StringComparison.Ordinal)
            || text.StartsWith("Could not run", StringComparison.Ordinal)
            || text.Contains("does NOT compile", StringComparison.Ordinal))
        {
            return ToolCallOutcome.Reported;
        }

        return ToolCallOutcome.Ok;
    }

    private static string Render(object? value) => value switch
    {
        null => "",
        string text => text,
        _ => value.ToString() ?? "",
    };

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + $"\n… ({text.Length - max} karakter lagi)";
}
