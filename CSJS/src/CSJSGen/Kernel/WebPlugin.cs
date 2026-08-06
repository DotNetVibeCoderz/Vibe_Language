using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace CSJSGen.Kernel;

/// <summary>
/// Kernel functions that reach the internet: search, page scraping, and fetching a file by URL.
/// Each one degrades to a clear message rather than an exception, because a tool failure should
/// be something the assistant can talk about, not something that ends the turn.
/// </summary>
public sealed class WebPlugin
{
    private readonly HttpClient _http;
    private readonly string _tavilyApiKey;

    public WebPlugin(HttpClient http, string tavilyApiKey)
    {
        _http = http;
        _tavilyApiKey = tavilyApiKey;
    }

    [KernelFunction("search")]
    [Description("Search the internet with Tavily and return the top results with short summaries. Use for current information the model may not have.")]
    public async Task<string> SearchAsync(
        [Description("The search query.")] string query,
        [Description("How many results to return, 1 to 10.")] int maxResults = 5)
    {
        if (_tavilyApiKey.Length == 0)
            return "Internet search is unavailable: no Tavily API key is configured. Set Tools.Tavily.ApiKey in app.config.";

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                api_key = _tavilyApiKey,
                query,
                max_results = Math.Clamp(maxResults, 1, 10),
                search_depth = "basic",
                include_answer = true,
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("https://api.tavily.com/search", content);

            if (!response.IsSuccessStatusCode)
                return $"Search failed with status {(int)response.StatusCode}.";

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var report = new StringBuilder();

            if (root.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String)
                report.AppendLine($"Summary: {answer.GetString()}").AppendLine();

            if (root.TryGetProperty("results", out var results))
            {
                var index = 1;
                foreach (var result in results.EnumerateArray())
                {
                    report.AppendLine($"{index++}. {Text(result, "title")}");
                    report.AppendLine($"   {Text(result, "url")}");
                    var snippet = Text(result, "content");
                    if (snippet.Length > 0)
                        report.AppendLine($"   {Truncate(snippet, 300)}");
                    report.AppendLine();
                }
            }

            return report.Length > 0 ? report.ToString() : "The search returned no results.";
        }
        catch (Exception ex)
        {
            return $"Search failed: {ex.Message}";
        }
    }

    [KernelFunction("scrape_page")]
    [Description("Fetch a web page and return its readable text with the HTML markup stripped.")]
    public async Task<string> ScrapePageAsync(
        [Description("The page URL.")] string url,
        [Description("Maximum characters to return.")] int maxLength = 8000)
    {
        if (!IsHttpUrl(url)) return "That does not look like an http or https URL.";

        try
        {
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return $"Could not fetch the page: HTTP {(int)response.StatusCode}.";

            var html = await response.Content.ReadAsStringAsync();
            var text = StripHtml(html);

            return text.Length <= maxLength
                ? text
                : text[..maxLength] + $"\n\n[truncated — {text.Length - maxLength} more characters]";
        }
        catch (Exception ex)
        {
            return $"Could not fetch the page: {ex.Message}";
        }
    }

    [KernelFunction("read_file_from_url")]
    [Description("Download a text file from a URL and return its contents verbatim. Use for source files, JSON, CSV, or documentation.")]
    public async Task<string> ReadFileFromUrlAsync(
        [Description("The file URL.")] string url,
        [Description("Maximum characters to return.")] int maxLength = 20000)
    {
        if (!IsHttpUrl(url)) return "That does not look like an http or https URL.";

        try
        {
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return $"Could not download the file: HTTP {(int)response.StatusCode}.";

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (IsBinary(contentType))
                return $"That URL serves {contentType}, which is binary and cannot be read as text.";

            var text = await response.Content.ReadAsStringAsync();
            return text.Length <= maxLength
                ? text
                : text[..maxLength] + $"\n\n[truncated — {text.Length - maxLength} more characters]";
        }
        catch (Exception ex)
        {
            return $"Could not download the file: {ex.Message}";
        }
    }

    private static bool IsHttpUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsBinary(string contentType)
        => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
           || contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
           || contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
           || contentType is "application/octet-stream" or "application/pdf" or "application/zip";

    /// <summary>
    /// Reduces HTML to readable text. Script and style content is removed entirely — leaving it
    /// in would fill the model's context with minified JavaScript rather than the page.
    /// </summary>
    private static string StripHtml(string html)
    {
        var text = Regex.Replace(html, @"<(script|style|noscript|svg)\b[^>]*>.*?</\1>", " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        text = Regex.Replace(text, @"<!--.*?-->", " ", RegexOptions.Singleline);

        // Block-level tags become newlines so paragraph structure survives.
        text = Regex.Replace(text, @"</(p|div|h[1-6]|li|tr|section|article|header|footer)>", "\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);

        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n\s*\n\s*\n+", "\n\n");

        return text.Trim();
    }

    private static string Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string Truncate(string text, int length)
        => text.Length <= length ? text : text[..length].TrimEnd() + "…";
}
