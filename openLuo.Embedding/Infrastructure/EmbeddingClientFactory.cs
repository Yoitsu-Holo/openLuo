using openLuo.Modules.Embedding.Core.Interfaces;
using openLuo.Modules.Embedding.Core.Models;
using openLuo.Modules.Embedding.Infrastructure.Clients;
using openLuo.Modules.Embedding.Infrastructure.Common;
using static openLuo.Infrastructure.Logging.Logger;

namespace openLuo.Modules.Embedding.Infrastructure;

public static class EmbeddingClientFactory
{
    public static IEmbeddingClient Create(EmbeddingConfig config)
    {
        if (!config.Enabled)
        {
            Info("embedding", "embedding route: provider=disabled, adapter=official reason=embedding disabled by config");
            return new MicrosoftAiEmbeddingClient(
                false,
                config.Provider,
                config.BaseUrl,
                "disabled",
                config.Model,
                config.TimeoutSeconds,
                config.MaxRetryAttempts,
                config.BaseDelayMs);
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            Warn("embedding", $"embedding disabled because provider={config.Provider} has no apiKey configured");
            return new MicrosoftAiEmbeddingClient(
                false,
                config.Provider,
                config.BaseUrl,
                "disabled",
                config.Model,
                config.TimeoutSeconds,
                config.MaxRetryAttempts,
                config.BaseDelayMs);
        }

        var decision = EmbeddingProviderRouting.DecideRoute(config);
        Info("embedding", $"embedding route: provider={config.Provider}, adapter=official reason={decision.Reason}");

        return new MicrosoftAiEmbeddingClient(
            true,
            config.Provider,
            config.BaseUrl,
            config.ApiKey,
            config.Model,
            config.TimeoutSeconds,
            config.MaxRetryAttempts,
            config.BaseDelayMs);
    }
}
