using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using openLuo.Core.Interfaces;
using openLuo.Infrastructure.Security;
using openLuo.Modules.Embedding.Core.Interfaces;
using OpenAI;
using OpenAI.Embeddings;
using MsAiEmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;
using OpenAiEmbeddingClient = OpenAI.Embeddings.EmbeddingClient;

namespace openLuo.Modules.Embedding.Infrastructure.Clients;

public sealed class MicrosoftAiEmbeddingClient : IEmbeddingClient
{
    private readonly MsAiEmbeddingGenerator _generator;
    private readonly IGameLogger _logger;
    private readonly string _provider;
    private readonly string _model;
    private readonly int _timeoutSeconds;
    private readonly int _maxRetryAttempts;
    private readonly int _baseDelayMs;

    public bool Enabled { get; }

    public MicrosoftAiEmbeddingClient(
        bool enabled,
        string provider,
        string baseUrl,
        string apiKey,
        IGameLogger logger,
        string model,
        int timeoutSeconds,
        int maxRetryAttempts,
        int baseDelayMs)
    {
        Enabled = enabled;
        _logger = logger;
        _provider = provider;
        _model = model;
        _timeoutSeconds = timeoutSeconds;
        _maxRetryAttempts = maxRetryAttempts;
        _baseDelayMs = baseDelayMs;
        _generator = enabled
            ? CreateGenerator(baseUrl, apiKey, model)
            : DisabledEmbeddingGenerator.Instance;
    }

    public MicrosoftAiEmbeddingClient(
        bool enabled,
        string provider,
        MsAiEmbeddingGenerator generator,
        IGameLogger logger,
        string model,
        int timeoutSeconds,
        int maxRetryAttempts,
        int baseDelayMs)
    {
        Enabled = enabled;
        _generator = generator;
        _logger = logger;
        _provider = provider;
        _model = model;
        _timeoutSeconds = timeoutSeconds;
        _maxRetryAttempts = maxRetryAttempts;
        _baseDelayMs = baseDelayMs;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (!Enabled)
            throw new InvalidOperationException("Embedding service is disabled.");

        var sanitized = PromptSanitizer.SanitizeForPrompt(text);
        var retries = Math.Max(0, _maxRetryAttempts);
        var delayBase = Math.Max(50, _baseDelayMs);

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _timeoutSeconds)));

                if (string.Equals(_provider, "Qwen", StringComparison.OrdinalIgnoreCase))
                    _logger.Warn("embedding", "Microsoft AI adapter does not send Qwen-specific encoding_format=float.");

                _logger.Debug("embedding", $"embedding request started: model={_model}, provider=microsoft.ai.openai");
                var sw = Stopwatch.StartNew();
                var embeddings = await _generator.GenerateAsync([sanitized], cancellationToken: linked.Token);
                var vector = embeddings[0].Vector.ToArray();
                sw.Stop();
                _logger.Debug("embedding", $"embedding request completed: duration={sw.ElapsedMilliseconds}ms, vectorLength={vector.Length}");
                return vector;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < retries)
            {
                _logger.Warn("embedding", $"embedding timeout, retrying {attempt + 1}/{retries}");
                await DelayWithBackoffAsync(attempt, delayBase, ct);
            }
            catch (HttpRequestException ex) when (attempt < retries)
            {
                _logger.Warn("embedding", $"embedding request failed, retrying {attempt + 1}/{retries}: {ex.Message}");
                await DelayWithBackoffAsync(attempt, delayBase, ct);
            }
        }

        throw new HttpRequestException($"Embedding request failed after {retries} retries");
    }

    private static MsAiEmbeddingGenerator CreateGenerator(string baseUrl, string apiKey, string model)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(baseUrl)
        };
        return new OpenAiEmbeddingClient(model, new ApiKeyCredential(apiKey), options).AsIEmbeddingGenerator();
    }

    private static async Task DelayWithBackoffAsync(int attempt, int baseDelayMs, CancellationToken ct)
    {
        var jitter = Random.Shared.Next(0, 100);
        var delay = Math.Min(8_000, (int)(baseDelayMs * Math.Pow(2, attempt)) + jitter);
        await Task.Delay(delay, ct);
    }

    private sealed class DisabledEmbeddingGenerator : MsAiEmbeddingGenerator
    {
        public static DisabledEmbeddingGenerator Instance { get; } = new();

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<Microsoft.Extensions.AI.GeneratedEmbeddings<Microsoft.Extensions.AI.Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            Microsoft.Extensions.AI.EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Embedding service is disabled.");
    }
}
