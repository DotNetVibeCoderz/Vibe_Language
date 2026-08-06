using CSJSGen.Kernel;
using CSJSGen.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CSJSGen.Services;

/// <summary>
/// Builds the Semantic Kernel for the selected provider and registers the plugins.
/// <para>
/// Provider choice is confined to <see cref="AddChatCompletion"/>; everything downstream —
/// plugins, chat history, streaming — is provider-agnostic, so adding a provider means adding
/// one case rather than a parallel code path.
/// </para>
/// </summary>
public sealed class KernelFactory
{
    private readonly HttpClient _http;

    public KernelFactory(HttpClient http) => _http = http;

    /// <summary>Builds a kernel wired to the configured provider and tools.</summary>
    public Microsoft.SemanticKernel.Kernel Build(AppSettings settings, EditorBridge editor)
    {
        var builder = Microsoft.SemanticKernel.Kernel.CreateBuilder();

        AddChatCompletion(builder, settings);

        var kernel = builder.Build();

        // Kernel functions are the assistant's extension point: everything Jack can do beyond
        // producing text is a function registered here.
        kernel.Plugins.AddFromObject(new CsjsPlugin(() => editor.GetCode(), code => editor.SetCode(code)), "Csjs");
        kernel.Plugins.AddFromObject(new UtilityPlugin(), "Utility");
        kernel.Plugins.AddFromObject(new WebPlugin(_http, settings.TavilyApiKey), "Web");

        return kernel;
    }

    private void AddChatCompletion(IKernelBuilder builder, AppSettings settings)
    {
        switch (settings.Provider)
        {
            case AiProvider.OpenAI:
                if (settings.Endpoint.Length > 0)
                {
                    builder.AddOpenAIChatCompletion(
                        modelId: settings.Model,
                        apiKey: settings.ApiKey,
                        endpoint: new Uri(settings.Endpoint),
                        httpClient: _http);
                }
                else
                {
                    builder.AddOpenAIChatCompletion(
                        modelId: settings.Model,
                        apiKey: settings.ApiKey,
                        httpClient: _http);
                }
                break;

            case AiProvider.AzureOpenAI:
                // Azure routes on deployment name rather than model id, which is why the two are
                // separate settings even when they hold the same value.
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: settings.Deployment.Length > 0 ? settings.Deployment : settings.Model,
                    endpoint: settings.Endpoint,
                    apiKey: settings.ApiKey,
                    modelId: settings.Model,
                    httpClient: _http);
                break;

            case AiProvider.Anthropic:
                // Semantic Kernel has no first-party Anthropic connector, but Anthropic serves an
                // OpenAI-compatible surface, so the OpenAI connector reaches it unchanged.
                builder.AddOpenAIChatCompletion(
                    modelId: settings.Model,
                    apiKey: settings.ApiKey,
                    endpoint: new Uri(settings.Endpoint.Length > 0 ? settings.Endpoint : "https://api.anthropic.com/v1"),
                    httpClient: _http);
                break;

            case AiProvider.DeepSeek:
                // DeepSeek is OpenAI-compatible; only the endpoint differs.
                builder.AddOpenAIChatCompletion(
                    modelId: settings.Model,
                    apiKey: settings.ApiKey,
                    endpoint: new Uri(settings.Endpoint.Length > 0 ? settings.Endpoint : "https://api.deepseek.com"),
                    httpClient: _http);
                break;

            case AiProvider.Gemini:
                builder.AddGoogleAIGeminiChatCompletion(
                    modelId: settings.Model,
                    apiKey: settings.ApiKey,
                    httpClient: _http);
                break;

            case AiProvider.Ollama:
                // Ollama runs locally and needs no key, which is why it is the fallback when
                // nothing else is configured.
                builder.AddOllamaChatCompletion(
                    modelId: settings.Model,
                    endpoint: new Uri(settings.Endpoint.Length > 0 ? settings.Endpoint : "http://localhost:11434"));
                break;

            default:
                throw new NotSupportedException($"Provider {settings.Provider} is not supported.");
        }
    }

    /// <summary>
    /// Builds the per-request settings, including automatic function calling — which is what
    /// lets Jack compile and run code rather than only describe it.
    /// </summary>
    public static PromptExecutionSettings BuildExecutionSettings(AppSettings settings)
    {
        return settings.Provider switch
        {
            AiProvider.Gemini => new GeminiPromptExecutionSettings
            {
                Temperature = settings.Temperature,
                MaxTokens = settings.MaxTokens,
                TopP = settings.TopP,
                ToolCallBehavior = GeminiToolCallBehavior.AutoInvokeKernelFunctions,
            },

            AiProvider.Ollama => new OllamaPromptExecutionSettings
            {
                Temperature = (float)settings.Temperature,
                TopP = (float)settings.TopP,
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            },

            _ when IsReasoningModel(settings.Model) => new OpenAIPromptExecutionSettings
            {
                // Reasoning models reject `temperature` and `top_p` outright — only the defaults
                // are accepted — and require `max_completion_tokens` rather than `max_tokens`,
                // since the budget has to cover reasoning tokens as well as the reply. Sending
                // the ordinary settings to one is a 400, not a degraded answer.
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                ExtraBody = new Dictionary<string, object?>
                {
                    // Reasoning consumes budget before a single visible token is produced, so
                    // this is raised well above the configured limit to leave room for a reply.
                    ["max_completion_tokens"] = Math.Max(settings.MaxTokens, 4096),
                },
            },

            _ => new OpenAIPromptExecutionSettings
            {
                Temperature = settings.Temperature,
                MaxTokens = settings.MaxTokens,
                TopP = settings.TopP,
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            },
        };
    }

    /// <summary>
    /// True for models that reason before answering. They share a distinct parameter contract, so
    /// sending the ordinary sampling settings to one is a 400 rather than a degraded answer.
    /// </summary>
    public static bool IsReasoningModel(string model)
    {
        var name = model.ToLowerInvariant();

        return name.StartsWith("o1", StringComparison.Ordinal)
            || name.StartsWith("o3", StringComparison.Ordinal)
            || name.StartsWith("o4", StringComparison.Ordinal)
            || name.StartsWith("gpt-5", StringComparison.Ordinal)
            || name.Contains("reasoner", StringComparison.Ordinal);
    }
}

/// <summary>
/// Lets kernel functions reach the editor without the plugin knowing about the UI.
/// The chat panel and the editor are separate components; this is the seam between them.
/// </summary>
public sealed class EditorBridge
{
    private Func<string?>? _read;
    private Action<string>? _write;

    public void Connect(Func<string?> read, Action<string> write)
    {
        _read = read;
        _write = write;
    }

    public string? GetCode() => _read?.Invoke();

    public void SetCode(string code) => _write?.Invoke(code);
}
