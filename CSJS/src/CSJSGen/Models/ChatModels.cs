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

/// <summary>How a kernel function call ended.</summary>
public enum ToolCallOutcome
{
    /// <summary>Still executing.</summary>
    Running,

    /// <summary>Returned, and what it returned reads as success.</summary>
    Ok,

    /// <summary>
    /// Returned normally but reported a failure — a compile error, a non-zero exit. The function
    /// did its job; the code it checked did not pass.
    /// </summary>
    Reported,

    /// <summary>Threw.</summary>
    Error,
}

/// <summary>
/// A record of one kernel function the assistant invoked, kept with the reply it belongs to so
/// the transcript shows what Jack did rather than only what he said.
/// <para>
/// Settable properties rather than a positional record: sessions are persisted as JSON, and a
/// field added later must deserialize to its default from a file written before it existed.
/// </para>
/// </summary>
public sealed class ToolCall
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..10];

    /// <summary>Plugin name as registered, e.g. <c>Csjs</c>.</summary>
    public string Plugin { get; set; } = "";

    /// <summary>Function name, e.g. <c>compile</c>.</summary>
    public string Function { get; set; } = "";

    /// <summary>Arguments as rendered for display, each value truncated.</summary>
    public Dictionary<string, string> Arguments { get; set; } = new();

    /// <summary>What the function returned (or the exception message), truncated for display.</summary>
    public string Result { get; set; } = "";

    public ToolCallOutcome Outcome { get; set; } = ToolCallOutcome.Running;

    public double DurationMs { get; set; }

    /// <summary>
    /// Length of the reply text when the call began. The transcript places the call at this
    /// point, so text before and after a tool step reads in the order it happened.
    /// </summary>
    public int Offset { get; set; }

    [JsonIgnore]
    public string QualifiedName => Plugin.Length > 0 ? $"{Plugin}.{Function}" : Function;
}

/// <summary>One message in a conversation.</summary>
public sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..12];
    public ChatRole Role { get; set; }
    public string Content { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public List<Attachment> Attachments { get; set; } = new();
    public List<ToolCall> ToolCalls { get; set; } = new();

    /// <summary>
    /// A stable copy of <see cref="ToolCalls"/>. Calls are recorded from whatever thread the
    /// kernel invokes functions on while the UI renders, so readers take a snapshot under the
    /// same lock the writers use.
    /// </summary>
    public ToolCall[] SnapshotToolCalls()
    {
        lock (ToolCalls) return ToolCalls.ToArray();
    }

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
