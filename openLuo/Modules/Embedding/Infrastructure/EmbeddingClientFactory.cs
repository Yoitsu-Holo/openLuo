using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Embedding.Core.Interfaces;
using openLuo.Modules.Embedding.Infrastructure.Clients;
using openLuo.Modules.Embedding.Infrastructure.Common;

namespace openLuo.Modules.Embedding.Infrastructure;

public static class EmbeddingClientFactory
{
    public static IEmbeddingClient Create(
        EmbeddingConfig config,
        IGameLogger logger)
    {
        if (!config.Enabled)
        {
            logger.Info("embedding", "embedding route: provider=disabled, adapter=official reason=embedding disabled by config");
            return new MicrosoftAiEmbeddingClient(
                false,
                config.Provider,
                config.BaseUrl,
                "disabled",
                logger,
                config.Model,
                config.TimeoutSeconds,
                config.MaxRetryAttempts,
                config.BaseDelayMs);
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            logger.Warn("embedding", $"embedding disabled because provider={config.Provider} has no apiKey configured");
            return new MicrosoftAiEmbeddingClient(
                false,
                config.Provider,
                config.BaseUrl,
                "disabled",
                logger,
                config.Model,
                config.TimeoutSeconds,
                config.MaxRetryAttempts,
                config.BaseDelayMs);
        }

        var decision = EmbeddingProviderRouting.DecideRoute(config);
        logger.Info("embedding", $"embedding route: provider={config.Provider}, adapter=official reason={decision.Reason}");

        return new MicrosoftAiEmbeddingClient(
            true,
            config.Provider,
            config.BaseUrl,
            config.ApiKey,
            logger,
            config.Model,
            config.TimeoutSeconds,
            config.MaxRetryAttempts,
            config.BaseDelayMs);
    }
}
