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

/// <summary>
/// 模型路由策略（D60）。rule = 纯能力匹配 + priority（零额外调用）；
/// llm = 候选多于一个时，先用路由模型做一次微型推理选择（失败回退 rule）。
/// </summary>
public class LlmRoutingConfig
{
    /// <summary>rule | llm</summary>
    public string Mode { get; set; } = "rule";

    /// <summary>llm 模式：用作路由器推理的 route 名称（必须在 routes[*].name 中）。</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>路由推理输出 token 上限（只输出 {"route": "..."}；推理型路由模型需要容纳 reasoning）。</summary>
    public int MaxTokens { get; set; } = 256;

    /// <summary>路由推理超时（秒）；超时回退 rule 路由。</summary>
    public double TimeoutSeconds { get; set; } = 8;

    public LlmRoutingConfig Clone() => new()
    {
        Mode = Mode,
        Model = Model,
        MaxTokens = MaxTokens,
        TimeoutSeconds = TimeoutSeconds
    };
}

public class LlmConfig
{
    public List<LlmRouteConfig> Routes { get; set; } = [];

    public LlmRoutingConfig Routing { get; set; } = new();

    public bool SupportsVision => Routes?.Any(route => route.Enabled && (route.Capabilities?.SupportsVision ?? false)) == true;

    public bool SupportsTools => Routes?.Any(route => route.Enabled && (route.Capabilities?.SupportsTools ?? false)) == true;

    public LlmConfig Clone() => new()
    {
        Routes = Routes?.Select(route => route.Clone()).ToList() ?? [],
        Routing = Routing?.Clone() ?? new()
    };
}
