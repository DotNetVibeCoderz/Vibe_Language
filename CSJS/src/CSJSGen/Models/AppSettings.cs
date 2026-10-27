using System.Configuration;
using System.Globalization;

namespace CSJSGen.Models;

/// <summary>AI providers the assistant can be pointed at.</summary>
public enum AiProvider
{
    OpenAI,
    AzureOpenAI,
    Anthropic,
    Gemini,
    DeepSeek,
    Ollama,
}

/// <summary>
/// Reads <c>app.config</c>. The spec requires persona, temperature, model, and the rest to live
/// in configuration rather than in code, so this is the only place those values are resolved.
/// </summary>
public sealed class AppSettings
{
    public AiProvider Provider { get; set; } = AiProvider.OpenAI;
    public string Model { get; set; } = "gpt-4o";
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "";

    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public double TopP { get; set; } = 0.95;
    public string SystemPrompt { get; set; } = "";

    public string TavilyApiKey { get; set; } = "";
    public string SessionStore { get; set; } = "";
    public string Theme { get; set; } = "dark";

    /// <summary>
    /// The Azure deployment name. Azure routes on deployment rather than model id, so the two
    /// are separate fields even though they are often given the same value.
    /// </summary>
    public string Deployment { get; set; } = "";

    /// <summary>
    /// Panel widths and visibility as the last session left them. Written separately from the
    /// rest by <see cref="SaveLayout"/>, so a drag never rewrites keys and a settings save
    /// never overwrites the layout with the values read at startup.
    /// </summary>
    public WorkspaceLayout Layout { get; set; } = new();

    /// <summary>True when the active provider has everything it needs to run.</summary>
    public bool IsConfigured => Provider switch
    {
        AiProvider.Ollama => true,
        AiProvider.AzureOpenAI => ApiKey.Length > 0 && Endpoint.Length > 0,
        _ => ApiKey.Length > 0,
    };

    public static AppSettings Load()
    {
        var settings = new AppSettings
        {
            Provider = ParseEnum(Read("Ai.Provider"), AiProvider.OpenAI),
            Temperature = ParseDouble(Read("Ai.Temperature"), 0.7),
            MaxTokens = ParseInt(Read("Ai.MaxTokens"), 4096),
            TopP = ParseDouble(Read("Ai.TopP"), 0.95),
            SystemPrompt = Read("Ai.SystemPrompt"),
            TavilyApiKey = Read("Tools.Tavily.ApiKey"),
            SessionStore = Read("App.SessionStore"),
            Theme = Read("App.Theme") is { Length: > 0 } theme ? theme : "dark",
            Layout = WorkspaceLayout.Load(Read),
        };

        settings.ApplyProviderSettings();
        return settings;
    }

    /// <summary>
    /// Resolves the model, key, and endpoint for whichever provider is selected.
    /// Keeping per-provider keys in config means switching providers does not mean re-entering
    /// credentials for the one you switched away from.
    /// </summary>
    public void ApplyProviderSettings()
    {
        var prefix = $"Ai.{Provider}";
        Model = Read($"{prefix}.Model") is { Length: > 0 } model ? model : DefaultModelFor(Provider);
        ApiKey = ResolveApiKey(prefix);
        Endpoint = Read($"{prefix}.Endpoint");

        // Azure defaults its deployment name to the model id, which is the common setup.
        Deployment = Read($"{prefix}.Deployment") is { Length: > 0 } deployment ? deployment : Model;

        if (Provider == AiProvider.Ollama && Endpoint.Length == 0)
            Endpoint = "http://localhost:11434";

        if (Provider == AiProvider.DeepSeek && Endpoint.Length == 0)
            Endpoint = "https://api.deepseek.com";
    }

    /// <summary>
    /// Reads the key from config, falling back to the provider's conventional environment
    /// variable. Config files get committed by accident; environment variables usually do not.
    /// </summary>
    private string ResolveApiKey(string prefix)
    {
        var fromConfig = Read($"{prefix}.ApiKey");
        if (fromConfig.Length > 0) return fromConfig;

        var variable = Provider switch
        {
            AiProvider.OpenAI => "OPENAI_API_KEY",
            AiProvider.AzureOpenAI => "AZURE_OPENAI_API_KEY",
            AiProvider.Anthropic => "ANTHROPIC_API_KEY",
            AiProvider.Gemini => "GEMINI_API_KEY",
            AiProvider.DeepSeek => "DEEPSEEK_API_KEY",
            _ => null,
        };

        return variable is null ? "" : Environment.GetEnvironmentVariable(variable) ?? "";
    }

    public static string DefaultModelFor(AiProvider provider) => provider switch
    {
        AiProvider.OpenAI => "gpt-4o",
        AiProvider.AzureOpenAI => "gpt-4o",
        AiProvider.Anthropic => "claude-sonnet-4-5",
        AiProvider.Gemini => "gemini-2.0-flash",
        AiProvider.DeepSeek => "deepseek-chat",
        AiProvider.Ollama => "llama3.2",
        _ => "gpt-4o",
    };

    /// <summary>Model choices offered in the settings panel, per provider.</summary>
    public static IReadOnlyList<string> ModelsFor(AiProvider provider) => provider switch
    {
        AiProvider.OpenAI => new[] { "gpt-4o", "gpt-4o-mini", "gpt-4.1", "o3-mini" },
        AiProvider.AzureOpenAI => new[] { "gpt-4o", "gpt-4o-mini", "gpt-5-mini", "gpt-4.1" },
        AiProvider.Anthropic => new[] { "claude-sonnet-4-5", "claude-opus-4-1", "claude-haiku-4-5" },
        AiProvider.Gemini => new[] { "gemini-2.0-flash", "gemini-2.0-pro", "gemini-1.5-pro" },
        AiProvider.DeepSeek => new[] { "deepseek-chat", "deepseek-reasoner", "deepseek-v4-flash" },
        AiProvider.Ollama => new[] { "llama3.2", "qwen2.5-coder", "deepseek-coder-v2", "phi4" },
        _ => Array.Empty<string>(),
    };

    /// <summary>Writes the current values back to <c>app.config</c>.</summary>
    public void Save()
    {
        var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        void Set(string key, string value)
        {
            if (config.AppSettings.Settings[key] is null) config.AppSettings.Settings.Add(key, value);
            else config.AppSettings.Settings[key].Value = value;
        }

        Set("Ai.Provider", Provider.ToString());
        Set($"Ai.{Provider}.Model", Model);
        Set($"Ai.{Provider}.ApiKey", ApiKey);
        Set($"Ai.{Provider}.Endpoint", Endpoint);
        Set($"Ai.{Provider}.Deployment", Deployment);
        Set("Ai.Temperature", Temperature.ToString(CultureInfo.InvariantCulture));
        Set("Ai.MaxTokens", MaxTokens.ToString(CultureInfo.InvariantCulture));
        Set("Ai.TopP", TopP.ToString(CultureInfo.InvariantCulture));
        Set("Ai.SystemPrompt", SystemPrompt);
        Set("Tools.Tavily.ApiKey", TavilyApiKey);
        Set("App.Theme", Theme);

        config.Save(ConfigurationSaveMode.Modified);
        ConfigurationManager.RefreshSection("appSettings");
    }

    /// <summary>
    /// Writes only the layout keys and the theme. Called on every settled layout change, so it
    /// deliberately leaves provider settings and keys alone.
    /// </summary>
    public static void SaveLayout(WorkspaceLayout layout, string theme)
    {
        lock (SaveLock)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            void Set(string key, string value)
            {
                if (config.AppSettings.Settings[key] is null) config.AppSettings.Settings.Add(key, value);
                else config.AppSettings.Settings[key].Value = value;
            }

            foreach (var (key, value) in layout.ToPairs()) Set(key, value);
            Set("App.Theme", theme);

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }

    // Layout saves arrive from a timer thread and from shutdown; two writers racing on the same
    // file would lose one of them.
    private static readonly object SaveLock = new();

    private static string Read(string key) => ConfigurationManager.AppSettings[key] ?? "";

    internal static T ParseEnum<T>(string value, T fallback) where T : struct
        => Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    internal static double ParseDouble(string value, double fallback)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    internal static int ParseInt(string value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}

/// <summary>
/// The workspace layout: which panels are showing and how wide they are. Bounds live here so a
/// hand-edited or stale config cannot restore a panel too narrow to grab or wide enough to
/// squeeze the editor out — the same bounds <see cref="AppState"/> applies to a drag.
/// </summary>
public sealed record WorkspaceLayout
{
    public const double ExplorerMin = 150, ExplorerMax = 480, ExplorerDefault = 232;
    public const double PropertiesMin = 160, PropertiesMax = 480, PropertiesDefault = 248;
    public const double ChatMin = 280, ChatMax = 720, ChatDefault = 400;

    public bool ShowExplorer { get; init; } = true;
    public bool ShowProperties { get; init; } = true;
    public bool ShowChat { get; init; } = true;
    public bool ShowLineNumbers { get; init; } = true;

    public double ExplorerWidth { get; init; } = ExplorerDefault;
    public double PropertiesWidth { get; init; } = PropertiesDefault;
    public double ChatWidth { get; init; } = ChatDefault;

    /// <summary>Reads the layout keys, falling back to defaults and clamping what it finds.</summary>
    public static WorkspaceLayout Load(Func<string, string> read) => new()
    {
        ShowExplorer = ParseBool(read("Layout.ShowExplorer"), true),
        ShowProperties = ParseBool(read("Layout.ShowProperties"), true),
        ShowChat = ParseBool(read("Layout.ShowChat"), true),
        ShowLineNumbers = ParseBool(read("Layout.ShowLineNumbers"), true),
        ExplorerWidth = Width(read("Layout.ExplorerWidth"), ExplorerDefault, ExplorerMin, ExplorerMax),
        PropertiesWidth = Width(read("Layout.PropertiesWidth"), PropertiesDefault, PropertiesMin, PropertiesMax),
        ChatWidth = Width(read("Layout.ChatWidth"), ChatDefault, ChatMin, ChatMax),
    };

    /// <summary>The layout as config keys and invariant-culture values.</summary>
    public IEnumerable<(string Key, string Value)> ToPairs()
    {
        yield return ("Layout.ShowExplorer", Bool(ShowExplorer));
        yield return ("Layout.ShowProperties", Bool(ShowProperties));
        yield return ("Layout.ShowChat", Bool(ShowChat));
        yield return ("Layout.ShowLineNumbers", Bool(ShowLineNumbers));
        yield return ("Layout.ExplorerWidth", Number(ExplorerWidth));
        yield return ("Layout.PropertiesWidth", Number(PropertiesWidth));
        yield return ("Layout.ChatWidth", Number(ChatWidth));
    }

    private static double Width(string value, double fallback, double min, double max)
    {
        var parsed = AppSettings.ParseDouble(value, fallback);

        // NaN and infinity parse successfully and would survive a clamp, so they are rejected
        // outright rather than producing a CSS track like "NaNpx".
        return double.IsFinite(parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }

    private static bool ParseBool(string value, bool fallback)
        => bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Number(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);
}
