namespace openLuo.Modules.Embedding.Core.Models;

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
