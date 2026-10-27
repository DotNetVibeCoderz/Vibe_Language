using System.Text.RegularExpressions;

namespace CsjsLib;

/// <summary>Small text helpers, callable from any JavaScript.</summary>
public static class Text
{
    /// <summary>"Hello, World!" becomes "hello-world".</summary>
    public static string Slugify(string title)
    {
        var lower = title.Trim().ToLowerInvariant();
        var dashed = Regex.Replace(lower, @"[^a-z0-9]+", "-");
        return dashed.Trim('-');
    }

    /// <summary>Counts words, treating any run of whitespace as one separator.</summary>
    public static int WordCount(string text)
        => text.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Shortens text to <paramref name="max"/> characters, ending with an ellipsis.</summary>
    public static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..Math.Max(0, max - 1)].TrimEnd() + "…";
}
