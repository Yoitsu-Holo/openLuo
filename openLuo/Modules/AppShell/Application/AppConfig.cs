namespace openLuo.Modules.AppShell.Application;

public enum LlmProvider
{
    OpenAI,
    Qwen,
    DeepSeek,
    Ollama
}

public class LlmConfig
{
    public LlmProvider Provider { get; set; } = LlmProvider.OpenAI;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public float Temperature { get; set; } = 0.7f;
    public int? MaxTokens { get; set; } = null;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxRetryAttempts { get; set; } = 5;
    public int BaseDelayMs { get; set; } = 50;
    public int RateLimitPerMinute { get; set; } = 100;
    public bool Streaming { get; set; } = false;
    public bool SupportsVision { get; set; } = false;

    public LlmConfig Clone() => new()
    {
        Provider = Provider,
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
        Model = Model,
        Temperature = Temperature,
        MaxTokens = MaxTokens,
        TimeoutSeconds = TimeoutSeconds,
        MaxRetryAttempts = MaxRetryAttempts,
        BaseDelayMs = BaseDelayMs,
        RateLimitPerMinute = RateLimitPerMinute,
        Streaming = Streaming,
        SupportsVision = SupportsVision
    };
}

public class EmbeddingConfig
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "Qwen";
    public string BaseUrl { get; set; } = "https://dashscope.aliyuncs.com/compatible-mode/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "Qwen3-Embedding-4B";
    public string EndpointPath { get; set; } = "embeddings";
    public int TimeoutSeconds { get; set; } = 8;
    public int MaxRetryAttempts { get; set; } = 3;
    public int BaseDelayMs { get; set; } = 300;

    public EmbeddingConfig Clone() => new()
    {
        Enabled = Enabled,
        Provider = Provider,
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
        Model = Model,
        EndpointPath = EndpointPath,
        TimeoutSeconds = TimeoutSeconds,
        MaxRetryAttempts = MaxRetryAttempts,
        BaseDelayMs = BaseDelayMs
    };
}

public class LogConfig
{
    /// <summary>off / error / warn / info / debug</summary>
    public string Level { get; set; } = "info";
    public bool OutputToConsole { get; set; } = false;
    public Dictionary<string, string> Categories { get; set; } = new();

    public LogConfig Clone() => new()
    {
        Level = Level,
        OutputToConsole = OutputToConsole,
        Categories = Categories is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(Categories, StringComparer.OrdinalIgnoreCase)
    };
}

public class SqliteVecConfig
{
    public string ExtensionPath { get; set; } = string.Empty;
    public int VectorDimensions { get; set; } = 2560;

    public SqliteVecConfig Clone() => new()
    {
        ExtensionPath = ExtensionPath,
        VectorDimensions = VectorDimensions
    };
}

public class MemoryRetrievalConfig
{
    public int CharacterTopK { get; set; } = 8;
    public int GlobalTopK { get; set; } = 4;
    public int RecentN { get; set; } = 12;
    public int EmotionalN { get; set; } = 8;
    public double? GlobalDistanceMax { get; set; } = 0.7;

    public MemoryRetrievalConfig Clone() => new()
    {
        CharacterTopK = CharacterTopK,
        GlobalTopK = GlobalTopK,
        RecentN = RecentN,
        EmotionalN = EmotionalN,
        GlobalDistanceMax = GlobalDistanceMax
    };
}

public class AgentRuntimeConfig
{
    public int ChatRoundTimeoutSeconds { get; set; } = 60;
    public int TaskDispatchTimeoutSeconds { get; set; } = 24;
    public int PendingAbilityConfirmTimeoutSeconds { get; set; } = 45;
    public int InvocationConfirmTimeoutSeconds { get; set; } = 30;
    public int ContextConversationRetainCount { get; set; } = 24;

    public AgentRuntimeConfig Clone() => new()
    {
        ChatRoundTimeoutSeconds = ChatRoundTimeoutSeconds,
        TaskDispatchTimeoutSeconds = TaskDispatchTimeoutSeconds,
        PendingAbilityConfirmTimeoutSeconds = PendingAbilityConfirmTimeoutSeconds,
        InvocationConfirmTimeoutSeconds = InvocationConfirmTimeoutSeconds,
        ContextConversationRetainCount = ContextConversationRetainCount
    };
}

public class InterAgentConfig
{
    public int AskTimeoutSeconds { get; set; } = 12;
    public int SessionTurnTimeoutSeconds { get; set; } = 12;
    public int HiddenDialogueFuseTurns { get; set; } = 24;

    public InterAgentConfig Clone() => new()
    {
        AskTimeoutSeconds = AskTimeoutSeconds,
        SessionTurnTimeoutSeconds = SessionTurnTimeoutSeconds,
        HiddenDialogueFuseTurns = HiddenDialogueFuseTurns
    };
}

public class PluginRuntimeConfig
{
    public int DefaultCommandTimeoutSeconds { get; set; } = 30;
    public int ChatCommandTimeoutSeconds { get; set; } = 120;
    public int HookCallTimeoutSeconds { get; set; } = 120;
    public int RpcDefaultTimeoutSeconds { get; set; } = 5;
    public int RpcHandshakeTimeoutSeconds { get; set; } = 15;
    public int HostRequestDefaultTimeoutSeconds { get; set; } = 30;
    public int ProcessShutdownGraceMs { get; set; } = 500;

    public PluginRuntimeConfig Clone() => new()
    {
        DefaultCommandTimeoutSeconds = DefaultCommandTimeoutSeconds,
        ChatCommandTimeoutSeconds = ChatCommandTimeoutSeconds,
        HookCallTimeoutSeconds = HookCallTimeoutSeconds,
        RpcDefaultTimeoutSeconds = RpcDefaultTimeoutSeconds,
        RpcHandshakeTimeoutSeconds = RpcHandshakeTimeoutSeconds,
        HostRequestDefaultTimeoutSeconds = HostRequestDefaultTimeoutSeconds,
        ProcessShutdownGraceMs = ProcessShutdownGraceMs
    };
}

public class SecurityRuntimeConfig
{
    public int RateLimitPerMinute { get; set; } = 10;
    public int MaxInputLength { get; set; } = 1000;
    public int BurstLimit { get; set; } = 5;
    public int PromptBreakCharLimit { get; set; } = 10;
    public int MaxImageSizeBytes { get; set; } = 10_485_760;
    public string AllowedImageMimeTypes { get; set; } = "image/png,image/jpeg,image/gif,image/webp";
    public int MaxBase64PromptLength { get; set; } = 1_048_576;
    public int ImageDownloadTimeoutSeconds { get; set; } = 15;
    public int ImageDownloadMaxRetries { get; set; } = 3;

    public SecurityRuntimeConfig Clone() => new()
    {
        RateLimitPerMinute = RateLimitPerMinute,
        MaxInputLength = MaxInputLength,
        BurstLimit = BurstLimit,
        PromptBreakCharLimit = PromptBreakCharLimit,
        MaxImageSizeBytes = MaxImageSizeBytes,
        AllowedImageMimeTypes = AllowedImageMimeTypes,
        MaxBase64PromptLength = MaxBase64PromptLength,
        ImageDownloadTimeoutSeconds = ImageDownloadTimeoutSeconds,
        ImageDownloadMaxRetries = ImageDownloadMaxRetries
    };
}

public class LifecycleConfig
{
    public int WakeUpBaseMinute { get; set; } = 480;
    public int LateSleepThresholdMinute { get; set; } = 120;
    public double MinimumRecoveryDelta { get; set; } = 20.0;

    public LifecycleConfig Clone() => new()
    {
        WakeUpBaseMinute = WakeUpBaseMinute,
        LateSleepThresholdMinute = LateSleepThresholdMinute,
        MinimumRecoveryDelta = MinimumRecoveryDelta
    };
}

public class MemoryStoreConfig
{
    public int EmbeddingOperationTimeoutSeconds { get; set; } = 8;
    public int CompressionLookbackDays { get; set; } = 30;
    public int CompressionCheckCount { get; set; } = 50;

    public MemoryStoreConfig Clone() => new()
    {
        EmbeddingOperationTimeoutSeconds = EmbeddingOperationTimeoutSeconds,
        CompressionLookbackDays = CompressionLookbackDays,
        CompressionCheckCount = CompressionCheckCount
    };
}

public class TimeoutPolicyConfig
{
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int ChatTimeoutSeconds { get; set; } = 120;

    public TimeoutPolicyConfig Clone() => new()
    {
        DefaultTimeoutSeconds = DefaultTimeoutSeconds,
        ChatTimeoutSeconds = ChatTimeoutSeconds
    };
}

public class ResiliencePolicyConfig
{
    public int LlmMaxRetryAttempts { get; set; } = 3;
    public int LlmRetryDelaySeconds { get; set; } = 1;
    public int DatabaseMaxRetryAttempts { get; set; } = 2;
    public int DatabaseRetryDelayMilliseconds { get; set; } = 500;

    public ResiliencePolicyConfig Clone() => new()
    {
        LlmMaxRetryAttempts = LlmMaxRetryAttempts,
        LlmRetryDelaySeconds = LlmRetryDelaySeconds,
        DatabaseMaxRetryAttempts = DatabaseMaxRetryAttempts,
        DatabaseRetryDelayMilliseconds = DatabaseRetryDelayMilliseconds
    };
}


public class PerExecutorConfig
{
    public float? Temperature { get; set; } = null;
    public int? MaxTokens { get; set; } = null;

    public PerExecutorConfig Clone() => new()
    {
        Temperature = Temperature,
        MaxTokens = MaxTokens
    };
}

public class ExecutorConfigs
{
    public PerExecutorConfig CharacterResponse { get; set; } = new();
    public PerExecutorConfig StateUpdate { get; set; } = new();
    public PerExecutorConfig ToolUse { get; set; } = new();
    public PerExecutorConfig GiftIntent { get; set; } = new();
    public PerExecutorConfig FlowRouting { get; set; } = new();
    public PerExecutorConfig TODOList { get; set; } = new();
    public PerExecutorConfig GoalExecution { get; set; } = new();

    public ExecutorConfigs Clone() => new()
    {
        CharacterResponse = CharacterResponse.Clone(),
        StateUpdate = StateUpdate.Clone(),
        ToolUse = ToolUse.Clone(),
        GiftIntent = GiftIntent.Clone(),
        FlowRouting = FlowRouting.Clone(),
        TODOList = TODOList.Clone(),
        GoalExecution = GoalExecution.Clone()
    };

    /// <summary>Fill any executor slot that still has null Temperature/MaxTokens with C# defaults.</summary>
    internal ExecutorConfigs EnsureDefaults()
    {
        CharacterResponse.Temperature ??= 0.7f;
        StateUpdate.Temperature ??= 0.1f;
        StateUpdate.MaxTokens ??= 2048;
        ToolUse.Temperature ??= 0.2f;
        ToolUse.MaxTokens ??= 2048;
        FlowRouting.Temperature ??= 0.0f;
        FlowRouting.MaxTokens ??= 512;
        GiftIntent.Temperature ??= 0.0f;
        GiftIntent.MaxTokens ??= 512;
        TODOList.Temperature ??= 0.2f;
        TODOList.MaxTokens ??= 2048;
        GoalExecution.Temperature ??= 0.2f;
        GoalExecution.MaxTokens ??= 2048;
        return this;
    }
}

public class AppConfig
{
    public LlmConfig Llm { get; set; } = new();
    public EmbeddingConfig Embedding { get; set; } = new();
    public string DatabasePath { get; set; } = string.Empty;
    public LogConfig Log { get; set; } = new();
    public SqliteVecConfig SqliteVec { get; set; } = new();
    public MemoryRetrievalConfig MemoryRetrieval { get; set; } = new();
    public AgentRuntimeConfig Agent { get; set; } = new();
    public InterAgentConfig InterAgent { get; set; } = new();
    public PluginRuntimeConfig PluginRuntime { get; set; } = new();
    public SecurityRuntimeConfig Security { get; set; } = new();
    public LifecycleConfig Lifecycle { get; set; } = new();
    public MemoryStoreConfig MemoryStore { get; set; } = new();
    public TimeoutPolicyConfig Timeouts { get; set; } = new();
    public ResiliencePolicyConfig Resilience { get; set; } = new();
    public ExecutorConfigs Executors { get; set; } = new();

    internal void Normalize()
    {
        Executors.EnsureDefaults();
    }

    public AppConfig Clone() => new()
    {
        Llm = Llm.Clone(),
        Embedding = Embedding.Clone(),
        DatabasePath = DatabasePath,
        Log = Log.Clone(),
        SqliteVec = SqliteVec.Clone(),
        MemoryRetrieval = MemoryRetrieval.Clone(),
        Agent = Agent.Clone(),
        InterAgent = InterAgent.Clone(),
        PluginRuntime = PluginRuntime.Clone(),
        Security = Security.Clone(),
        Lifecycle = Lifecycle.Clone(),
        MemoryStore = MemoryStore.Clone(),
        Timeouts = Timeouts.Clone(),
        Resilience = Resilience.Clone(),
        Executors = Executors.Clone(),
    };
}
