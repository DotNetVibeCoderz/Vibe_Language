using System.Text.Json.Serialization;

namespace CSJSGen.Models;

/// <summary>Who produced a message.</summary>
public enum ChatRole
{
    User,
    Assistant,
    System,
    Tool,
}

/// <summary>An image or document the user attached to a message.</summary>
/// <param name="FileName">Original file name, shown in the transcript.</param>
/// <param name="Url">Where the file now lives — a local URL after upload.</param>
/// <param name="ContentType">MIME type, used to decide image versus document handling.</param>
/// <param name="SizeBytes">Size on disk.</param>
public sealed record Attachment(string FileName, string Url, string ContentType, long SizeBytes)
{
    /// <summary>
    /// True when this should be sent as an image content part rather than a link.
    /// The spec draws exactly this distinction: images become image content, documents
    /// become a link appended to the text.
    /// </summary>
    [JsonIgnore]
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string DisplaySize => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        _ => $"{SizeBytes / (1024.0 * 1024):F1} MB",
    };
}

/// <summary>A record of one kernel function the assistant invoked.</summary>
/// <param name="Name">Function name, e.g. <c>Csjs_Compile</c>.</param>
/// <param name="Arguments">Arguments as rendered for display.</param>
/// <param name="Result">What the function returned, truncated for display.</param>
/// <param name="Succeeded">False when the call threw.</param>
public sealed record ToolCall(string Name, string Arguments, string Result, bool Succeeded);

/// <summary>One message in a conversation.</summary>
public sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..12];
    public ChatRole Role { get; set; }
    public string Content { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public List<Attachment> Attachments { get; set; } = new();
    public List<ToolCall> ToolCalls { get; set; } = new();

    /// <summary>Set while a response is still arriving, so the UI can show a cursor.</summary>
    [JsonIgnore]
    public bool IsStreaming { get; set; }

    /// <summary>Set when the request failed, so the UI can offer a retry.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// A conversation. Sessions are independent: creating, deleting, or resetting one leaves the
/// others untouched, which is what the spec asks for.
/// </summary>
public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..12];
    public string Title { get; set; } = "Sesi baru";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonIgnore]
    public bool IsEmpty => Messages.Count == 0;

    /// <summary>
    /// Derives a title from the first user message, so the session list is scannable without
    /// making the user name every conversation.
    /// </summary>
    public void UpdateTitleFromFirstMessage()
    {
        if (Title != "Sesi baru") return;

        var first = Messages.FirstOrDefault(m => m.Role == ChatRole.User);
        if (first is null || first.Content.Length == 0) return;

        var line = first.Content.Split('\n')[0].Trim();
        Title = line.Length <= 42 ? line : line[..42].TrimEnd() + "…";
    }

    /// <summary>Clears the transcript but keeps the session, matching the spec's "reset".</summary>
    public void Reset()
    {
        Messages.Clear();
        Title = "Sesi baru";
        UpdatedAt = DateTimeOffset.Now;
    }
}

/// <summary>A ready-made prompt offered when a session is empty.</summary>
/// <param name="Category">Grouping shown above the card.</param>
/// <param name="Title">Short label.</param>
/// <param name="Prompt">The text placed into the composer.</param>
/// <param name="Icon">A single emoji used as the card's mark.</param>
public sealed record PromptTemplate(string Category, string Title, string Prompt, string Icon);
