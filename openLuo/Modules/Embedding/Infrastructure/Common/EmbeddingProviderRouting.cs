using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Embedding.Infrastructure.Common;

public static class EmbeddingProviderRouting
{
    public static EmbeddingRouteDecision DecideRoute(EmbeddingConfig config)
    {
        var provider = (config.Provider ?? string.Empty).Trim();
        if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            return new EmbeddingRouteDecision(true, "OpenAI embedding is backed by Microsoft.Extensions.AI.OpenAI.");

        if (provider.Equals("Qwen", StringComparison.OrdinalIgnoreCase))
            return new EmbeddingRouteDecision(true, "Qwen embedding is unified onto the official OpenAI-compatible adapter; encoding_format=float shaping is ignored.");

        return new EmbeddingRouteDecision(true, $"Provider '{provider}' is routed through the official OpenAI-compatible embedding adapter.");
    }
}
