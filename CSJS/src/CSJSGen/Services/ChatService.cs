using System.Text;
using System.Text.Json;
using CSJSGen.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SkChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace CSJSGen.Services;

/// <summary>
/// Owns the assistant's conversations: session lifecycle, message dispatch, and persistence.
/// <para>
/// The chat loop here is deliberately thin. Everything Jack can *do* lives in kernel functions,
/// so extending the assistant never means editing this class.
/// </para>
/// </summary>
public sealed class ChatService
{
    private readonly KernelFactory _kernelFactory;
    private readonly EditorBridge _editor;
    private readonly string _storePath;

    private Microsoft.SemanticKernel.Kernel? _kernel;
    private AppSettings _settings;

    // The reply currently being written. Tool calls the kernel makes while it streams are
    // recorded into it; null between sends, so a stray invocation records nothing.
    private ChatMessage? _activeReply;
    private readonly ToolCallRecorder _recorder;

    public ChatService(KernelFactory kernelFactory, EditorBridge editor, AppSettings settings)
    {
        _kernelFactory = kernelFactory;
        _editor = editor;
        _settings = settings;
        _recorder = new ToolCallRecorder(() => _activeReply, () => Changed?.Invoke());

        _storePath = settings.SessionStore.Length > 0
            ? settings.SessionStore
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CSJSGen", "sessions.json");

        Sessions = Load();
        if (Sessions.Count == 0) Sessions.Add(new ChatSession());
        ActiveSession = Sessions[0];
    }

    public List<ChatSession> Sessions { get; }
    public ChatSession ActiveSession { get; private set; }

    /// <summary>Raised whenever the transcript changes, so the UI can re-render.</summary>
    public event Action? Changed;

    /// <summary>True while a response is in flight.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Set when the last send failed, for display in the composer.</summary>
    public string? LastError { get; private set; }

    // ---------------------------------------------------------------------------------------
    // Session lifecycle
    // ---------------------------------------------------------------------------------------

    public ChatSession CreateSession()
    {
        var session = new ChatSession();
        Sessions.Insert(0, session);
        ActiveSession = session;
        Save();
        Changed?.Invoke();
        return session;
    }

    public void SelectSession(string id)
    {
        var session = Sessions.FirstOrDefault(s => s.Id == id);
        if (session is null) return;
        ActiveSession = session;
        Changed?.Invoke();
    }

    public void DeleteSession(string id)
    {
        var session = Sessions.FirstOrDefault(s => s.Id == id);
        if (session is null) return;

        Sessions.Remove(session);

        // There is always at least one session, so deleting the last one starts a fresh one.
        if (Sessions.Count == 0) Sessions.Add(new ChatSession());
        if (ActiveSession.Id == id) ActiveSession = Sessions[0];

        Save();
        Changed?.Invoke();
    }

    /// <summary>Clears the active session's transcript without deleting the session.</summary>
    public void ResetSession()
    {
        ActiveSession.Reset();
        Save();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------------------------------
    // Settings
    // ---------------------------------------------------------------------------------------

    /// <summary>Applies new settings and discards the kernel so the next send rebuilds it.</summary>
    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _kernel = null;
        Changed?.Invoke();
    }

    public AppSettings Settings => _settings;

    // ---------------------------------------------------------------------------------------
    // Sending
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Sends a message and streams the reply into the transcript.
    /// </summary>
    public async Task SendAsync(string text, IReadOnlyList<Attachment> attachments, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        if (text.Trim().Length == 0 && attachments.Count == 0) return;

        LastError = null;

        var userMessage = new ChatMessage
        {
            Role = ChatRole.User,
            Content = text.Trim(),
            Attachments = attachments.ToList(),
        };

        ActiveSession.Messages.Add(userMessage);
        ActiveSession.UpdateTitleFromFirstMessage();
        ActiveSession.UpdatedAt = DateTimeOffset.Now;

        var reply = new ChatMessage { Role = ChatRole.Assistant, IsStreaming = true };
        ActiveSession.Messages.Add(reply);

        IsBusy = true;
        _activeReply = reply;
        Changed?.Invoke();

        try
        {
            if (!_settings.IsConfigured)
            {
                throw new InvalidOperationException(
                    $"No API key is configured for {_settings.Provider}. Open Settings to add one, " +
                    "or switch to Ollama to run a local model.");
            }

            if (_kernel is null)
            {
                _kernel = _kernelFactory.Build(_settings, _editor);
                _kernel.FunctionInvocationFilters.Add(_recorder);
            }

            var chat = _kernel.GetRequiredService<IChatCompletionService>();
            var history = BuildHistory();
            var executionSettings = KernelFactory.BuildExecutionSettings(_settings);

            var buffer = new StringBuilder();
            var callsSeen = 0;

            await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(
                               history, executionSettings, _kernel, cancellationToken))
            {
                if (chunk.Content is not { Length: > 0 } content) continue;

                // Text after a tool step belongs to a new model turn. Without a break it would
                // run straight on from the sentence before the call.
                var calls = reply.SnapshotToolCalls().Length;
                if (calls > callsSeen)
                {
                    callsSeen = calls;
                    if (buffer.Length > 0 && !buffer.ToString().EndsWith("\n\n", StringComparison.Ordinal))
                        buffer.Append("\n\n");
                }

                buffer.Append(content);
                reply.Content = buffer.ToString();
                Changed?.Invoke();
            }

            reply.Content = buffer.ToString();

            if (reply.Content.Length == 0 && reply.ToolCalls.Count == 0)
                reply.Content = "(The model returned an empty response.)";
        }
        catch (OperationCanceledException)
        {
            reply.Content += reply.Content.Length > 0 ? "\n\n_(dihentikan)_" : "_(dihentikan)_";
        }
        catch (Exception ex)
        {
            // The failure belongs in the transcript, where the user can see and retry it.
            reply.Error = ex.Message;
            reply.Content = $"**Gagal menghubungi model.**\n\n```\n{ex.Message}\n```";
            LastError = ex.Message;
        }
        finally
        {
            _activeReply = null;
            reply.IsStreaming = false;
            IsBusy = false;
            ActiveSession.UpdatedAt = DateTimeOffset.Now;
            Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Removes the last exchange so the user can edit and resend.</summary>
    public void RetryLast()
    {
        var messages = ActiveSession.Messages;
        if (messages.Count == 0) return;

        if (messages[^1].Role == ChatRole.Assistant) messages.RemoveAt(messages.Count - 1);
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Builds the history sent to the model.
    /// <para>
    /// Attachments follow the split the spec calls for: an image becomes an image content part,
    /// while a document becomes a link appended to the text — a model can look at the former and
    /// only fetch the latter.
    /// </para>
    /// </summary>
    private ChatHistory BuildHistory()
    {
        var history = new ChatHistory();

        if (_settings.SystemPrompt.Length > 0)
            history.AddSystemMessage(_settings.SystemPrompt);

        foreach (var message in ActiveSession.Messages)
        {
            // The in-flight reply has nothing to contribute yet.
            if (message.IsStreaming) continue;
            if (message.Role == ChatRole.Assistant && message.Error is not null) continue;

            switch (message.Role)
            {
                case ChatRole.User:
                    history.Add(BuildUserMessage(message));
                    break;

                case ChatRole.Assistant:
                    if (message.Content.Length > 0) history.AddAssistantMessage(message.Content);
                    break;
            }
        }

        return history;
    }

    private static SkChatMessageContent BuildUserMessage(ChatMessage message)
    {
        var images = message.Attachments.Where(a => a.IsImage).ToList();
        var documents = message.Attachments.Where(a => !a.IsImage).ToList();

        var text = new StringBuilder(message.Content);

        if (documents.Count > 0)
        {
            text.AppendLine().AppendLine();
            text.AppendLine("Lampiran dokumen:");
            foreach (var document in documents)
                text.AppendLine($"- {document.FileName} ({document.DisplaySize}): {document.Url}");
        }

        if (images.Count == 0)
            return new SkChatMessageContent(AuthorRole.User, text.ToString());

        var items = new ChatMessageContentItemCollection
        {
            new TextContent(text.ToString()),
        };

        foreach (var image in images)
            items.Add(new ImageContent(new Uri(image.Url)));

        return new SkChatMessageContent(AuthorRole.User, items);
    }

    // ---------------------------------------------------------------------------------------
    // Persistence
    // ---------------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private List<ChatSession> Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return new List<ChatSession>();
            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<List<ChatSession>>(json, SerializerOptions) ?? new List<ChatSession>();
        }
        catch
        {
            // A corrupt store must not stop the app from opening; a fresh list is recoverable.
            return new List<ChatSession>();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            File.WriteAllText(_storePath, JsonSerializer.Serialize(Sessions, SerializerOptions));
        }
        catch
        {
            // Persistence is a convenience; failing to write should not interrupt a conversation.
        }
    }
}
