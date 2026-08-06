using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.SemanticKernel;

namespace CSJSGen.Kernel;

/// <summary>
/// General-purpose kernel functions: date and time, arithmetic, and text utilities.
/// <para>
/// These exist because language models are unreliable at exactly these things — today's date,
/// arithmetic that has to be right — and a tool call is cheaper than a wrong answer.
/// </para>
/// </summary>
public sealed class UtilityPlugin
{
    [KernelFunction("current_datetime")]
    [Description("Get the current date and time. Use this rather than guessing what today is.")]
    public string CurrentDateTime(
        [Description("IANA time zone id, e.g. Asia/Jakarta. Omit for the machine's local zone.")]
        string? timeZone = null)
    {
        var now = DateTimeOffset.Now;
        var zoneName = TimeZoneInfo.Local.DisplayName;

        if (!string.IsNullOrWhiteSpace(timeZone))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
                now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
                zoneName = zone.DisplayName;
            }
            catch (TimeZoneNotFoundException)
            {
                return $"Unknown time zone '{timeZone}'. Use an IANA id such as Asia/Jakarta.";
            }
        }

        var indonesian = new CultureInfo("id-ID");
        return $"""
            ISO 8601: {now:yyyy-MM-ddTHH:mm:sszzz}
            Readable: {now.ToString("dddd, d MMMM yyyy HH:mm", indonesian)}
            Day of week: {now.DayOfWeek}
            Day of year: {now.DayOfYear}
            Week: {ISOWeek.GetWeekOfYear(now.DateTime)}
            Zone: {zoneName}
            Unix seconds: {now.ToUnixTimeSeconds()}
            """;
    }

    [KernelFunction("date_difference")]
    [Description("Calculate the span between two dates, in days, months, and years.")]
    public string DateDifference(
        [Description("The earlier date, as yyyy-MM-dd.")] string from,
        [Description("The later date, as yyyy-MM-dd. Omit for today.")] string? to = null)
    {
        if (!DateTime.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return $"Could not read '{from}' as a date. Use yyyy-MM-dd.";

        var end = DateTime.Today;
        if (!string.IsNullOrWhiteSpace(to)
            && !DateTime.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
        {
            return $"Could not read '{to}' as a date. Use yyyy-MM-dd.";
        }

        var span = end - start;
        var years = end.Year - start.Year;
        var months = years * 12 + end.Month - start.Month;

        // Calendar months are only whole once the day-of-month has been reached.
        if (end.Day < start.Day) months--;
        if (end < start.AddYears(years)) years--;

        return $"""
            From {start:yyyy-MM-dd} to {end:yyyy-MM-dd}
            Total days: {span.Days}
            Whole months: {months}
            Whole years: {years}
            """;
    }

    [KernelFunction("calculate")]
    [Description("Evaluate an arithmetic expression. Supports + - * / % and parentheses. Use this instead of doing arithmetic yourself.")]
    public string Calculate(
        [Description("The expression, e.g. (1250 * 12) / 4.")] string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "No expression was supplied.";

        // Only arithmetic is permitted; DataTable.Compute would otherwise accept far more.
        foreach (var ch in expression)
        {
            if (!char.IsDigit(ch) && !"+-*/%(). \t".Contains(ch))
                return $"'{ch}' is not allowed. Only numbers, + - * / % and parentheses are supported.";
        }

        try
        {
            var result = new DataTable().Compute(expression, null);
            if (result is null or DBNull) return "The expression could not be evaluated.";

            var value = Convert.ToDouble(result, CultureInfo.InvariantCulture);
            return double.IsInteger(value)
                ? $"{expression} = {value:F0}"
                : $"{expression} = {value.ToString("G15", CultureInfo.InvariantCulture)}";
        }
        catch (Exception ex)
        {
            return $"Could not evaluate '{expression}': {ex.Message}";
        }
    }

    [KernelFunction("statistics")]
    [Description("Compute count, sum, mean, median, min, max, and standard deviation for a list of numbers.")]
    public string Statistics(
        [Description("Numbers separated by commas or spaces.")] string numbers)
    {
        var values = numbers
            .Split(new[] { ',', ' ', '\n', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => double.TryParse(token.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? (double?)v
                : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (values.Count == 0) return "No numbers could be read from that input.";

        var sorted = values.OrderBy(v => v).ToList();
        var mean = values.Average();

        var median = sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

        // Sample standard deviation; a single value has no spread to speak of.
        var stdDev = values.Count > 1
            ? Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1))
            : 0;

        return $"""
            Count: {values.Count}
            Sum: {values.Sum():G15}
            Mean: {mean:G15}
            Median: {median:G15}
            Min: {sorted[0]:G15}
            Max: {sorted[^1]:G15}
            Std dev (sample): {stdDev:G15}
            """;
    }

    [KernelFunction("format_number")]
    [Description("Format a number as Indonesian rupiah, a percentage, or with thousands separators.")]
    public string FormatNumber(
        [Description("The number to format.")] double value,
        [Description("One of: currency, percent, thousands.")] string style = "thousands")
    {
        var indonesian = new CultureInfo("id-ID");
        return style.ToLowerInvariant() switch
        {
            "currency" => value.ToString("C0", indonesian),
            "percent" => (value / 100).ToString("P2", indonesian),
            _ => value.ToString("N2", indonesian),
        };
    }

    [KernelFunction("count_tokens_estimate")]
    [Description("Roughly estimate how many tokens a piece of text will use, to judge whether it fits in context.")]
    public string EstimateTokens(
        [Description("The text to measure.")] string text)
    {
        // Around four characters per token is the usual rule of thumb for English and code.
        var estimate = (int)Math.Ceiling(text.Length / 4.0);
        var words = text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

        return $"""
            Characters: {text.Length}
            Words: {words}
            Lines: {text.Split('\n').Length}
            Estimated tokens: ~{estimate}
            """;
    }

    [KernelFunction("encode")]
    [Description("Encode or decode text as base64 or URL encoding.")]
    public string Encode(
        [Description("The text to transform.")] string text,
        [Description("One of: base64, base64-decode, url, url-decode.")] string mode)
    {
        try
        {
            return mode.ToLowerInvariant() switch
            {
                "base64" => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
                "base64-decode" => Encoding.UTF8.GetString(Convert.FromBase64String(text)),
                "url" => Uri.EscapeDataString(text),
                "url-decode" => Uri.UnescapeDataString(text),
                _ => $"Unknown mode '{mode}'. Use base64, base64-decode, url, or url-decode.",
            };
        }
        catch (Exception ex)
        {
            return $"Could not transform the text: {ex.Message}";
        }
    }
}
