using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Embedding.Core.Interfaces;

namespace openLuo.Modules.Embedding.Infrastructure;

public sealed class RuntimeConfiguredEmbeddingClient : IEmbeddingClient
{
    private readonly IRuntimeConfigCenter _configCenter;
    private readonly IGameLogger _logger;
    private readonly object _gate = new();
    private string? _cacheKey;
    private IEmbeddingClient? _inner;

    public RuntimeConfiguredEmbeddingClient(IRuntimeConfigCenter configCenter, IGameLogger logger)
    {
        _configCenter = configCenter;
        _logger = logger;
    }

    public bool Enabled => GetInner().Enabled;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        GetInner().EmbedAsync(text, ct);

    private IEmbeddingClient GetInner()
    {
        var config = _configCenter.GetSnapshot().Embedding;
        var key = string.Join("|",
            config.Enabled.ToString(),
            config.Provider,
            config.BaseUrl,
            config.ApiKey,
            config.Model,
            config.EndpointPath,
            config.TimeoutSeconds.ToString(),
            config.MaxRetryAttempts.ToString(),
            config.BaseDelayMs.ToString());

        lock (_gate)
        {
            if (_inner is not null && string.Equals(_cacheKey, key, StringComparison.Ordinal))
                return _inner;

            _cacheKey = key;
            _inner = EmbeddingClientFactory.Create(config, _logger);
            return _inner;
        }
    }
}
