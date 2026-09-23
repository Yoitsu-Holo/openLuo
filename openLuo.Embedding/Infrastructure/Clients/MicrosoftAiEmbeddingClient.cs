using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using openLuo.Infrastructure.Security;
using openLuo.Modules.Embedding.Core.Interfaces;
using OpenAI;
using static openLuo.Infrastructure.Logging.Logger;
using MsAiEmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;
using OpenAiEmbeddingClient = OpenAI.Embeddings.EmbeddingClient;

namespace openLuo.Modules.Embedding.Infrastructure.Clients;

public sealed class MicrosoftAiEmbeddingClient : IEmbeddingClient
{
    private readonly MsAiEmbeddingGenerator _generator;
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
        string model,
        int timeoutSeconds,
        int maxRetryAttempts,
        int baseDelayMs)
    {
        Enabled = enabled;
        _provider = provider;
        _model = model;
        _timeoutSeconds = timeoutSeconds;
        _maxRetryAttempts = maxRetryAttempts;
        _baseDelayMs = baseDelayMs;
        _generator = enabled
            ? CreateGenerator(baseUrl, apiKey, model)
            : DisabledEmbeddingGenerator.Instance;
    }

    internal MicrosoftAiEmbeddingClient(
        bool enabled,
        string provider,
        MsAiEmbeddingGenerator generator,
        string model,
        int timeoutSeconds,
        int maxRetryAttempts,
        int baseDelayMs)
    {
        Enabled = enabled;
        _generator = generator;
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
                    Warn("embedding", "Microsoft AI adapter does not send Qwen-specific encoding_format=float.");

                Debug("embedding", $"embedding request started: model={_model}, provider=microsoft.ai.openai");
                var sw = Stopwatch.StartNew();
                var embeddings = await _generator.GenerateAsync([sanitized], cancellationToken: linked.Token);
                var vector = embeddings[0].Vector.ToArray();
                sw.Stop();
                Debug("embedding", $"embedding request completed: duration={sw.ElapsedMilliseconds}ms, vectorLength={vector.Length}");
                return vector;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < retries)
            {
                Warn("embedding", $"embedding timeout, retrying {attempt + 1}/{retries}");
                await DelayWithBackoffAsync(attempt, delayBase, ct);
            }
            catch (HttpRequestException ex) when (attempt < retries)
            {
                Warn("embedding", $"embedding request failed, retrying {attempt + 1}/{retries}: {ex.Message}");
                await DelayWithBackoffAsync(attempt, delayBase, ct);
            }
        }

        throw new HttpRequestException($"Embedding request failed after {retries} retries");
    }

    private static MsAiEmbeddingGenerator CreateGenerator(string baseUrl, string apiKey, string model)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Trim().EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? baseUrl + "/"
                : baseUrl.TrimEnd('/') + "/")
        };
        var client = new OpenAiEmbeddingClient(model, new ApiKeyCredential(apiKey), options);
        return client.AsIEmbeddingGenerator();
    }

    private static async Task DelayWithBackoffAsync(int attempt, int baseDelayMs, CancellationToken ct)
    {
        var jitter = Random.Shared.Next(0, 100);
        await Task.Delay(Math.Min(10_000, (int)(baseDelayMs * Math.Pow(2, attempt)) + jitter), ct);
    }

    private sealed class DisabledEmbeddingGenerator : MsAiEmbeddingGenerator
    {
        public static readonly DisabledEmbeddingGenerator Instance = new();

        public void Dispose() { }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Embedding service is disabled.");
    }
}
