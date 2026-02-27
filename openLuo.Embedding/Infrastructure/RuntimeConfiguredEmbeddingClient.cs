using openLuo.Modules.Embedding.Core.Interfaces;
using openLuo.Modules.Embedding.Core.Models;
using openLuo.Modules.Embedding.Infrastructure.Clients;
using openLuo.Modules.Embedding.Infrastructure.Common;
using static openLuo.Infrastructure.Logging.Logger;

namespace openLuo.Modules.Embedding.Infrastructure;

public sealed class RuntimeConfiguredEmbeddingClient : IEmbeddingClient
{
    private readonly Func<EmbeddingConfig> _configProvider;
    private readonly object _gate = new();
    private string? _cacheKey;
    private IEmbeddingClient? _inner;

    public bool Enabled => GetInner().Enabled;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        GetInner().EmbedAsync(text, ct);

    public RuntimeConfiguredEmbeddingClient(Func<EmbeddingConfig> configProvider)
    {
        _configProvider = configProvider;
    }

    private IEmbeddingClient GetInner()
    {
        var config = _configProvider();
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
            _inner = EmbeddingClientFactory.Create(config);
            return _inner;
        }
    }
}
