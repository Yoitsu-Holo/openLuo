namespace openLuo.Modules.Llm.Core.Models;

public enum LlmProvider
{
    OpenAI,
    Qwen,
    DeepSeek,
    Ollama
}

public class LlmCapabilitiesConfig
{
    public bool SupportsVision { get; set; } = false;
    public bool SupportsJsonMode { get; set; } = true;
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsTools { get; set; } = false;

    public LlmCapabilitiesConfig Clone() => new()
    {
        SupportsVision = SupportsVision,
        SupportsJsonMode = SupportsJsonMode,
        SupportsStreaming = SupportsStreaming,
        SupportsTools = SupportsTools
    };
}

public class LlmRouteConfig
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 0;
    public LlmProvider Provider { get; set; } = LlmProvider.OpenAI;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public float Temperature { get; set; } = 0.7f;
    public int? MaxTokens { get; set; } = null;
    public bool Streaming { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxRetryAttempts { get; set; } = 5;
    public int BaseDelayMs { get; set; } = 50;
    public int RateLimitPerMinute { get; set; } = 100;
    public LlmCapabilitiesConfig Capabilities { get; set; } = new();

    public LlmRouteConfig Clone() => new()
    {
        Name = Name,
        Enabled = Enabled,
        Priority = Priority,
        Provider = Provider,
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
        Model = Model,
        Temperature = Temperature,
        MaxTokens = MaxTokens,
        Streaming = Streaming,
        TimeoutSeconds = TimeoutSeconds,
        MaxRetryAttempts = MaxRetryAttempts,
        BaseDelayMs = BaseDelayMs,
        RateLimitPerMinute = RateLimitPerMinute,
        Capabilities = Capabilities?.Clone() ?? new()
    };
}

public class LlmConfig
{
    public List<LlmRouteConfig> Routes { get; set; } = [];

    public bool SupportsVision => Routes?.Any(route => route.Enabled && (route.Capabilities?.SupportsVision ?? false)) == true;

    public bool SupportsTools => Routes?.Any(route => route.Enabled && (route.Capabilities?.SupportsTools ?? false)) == true;

    public LlmConfig Clone() => new()
    {
        Routes = Routes?.Select(route => route.Clone()).ToList() ?? []
    };
}
