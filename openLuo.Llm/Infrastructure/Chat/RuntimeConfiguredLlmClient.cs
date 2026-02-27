using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using static openLuo.Infrastructure.Logging.Logger;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Modules.Llm.Infrastructure.Chat;

public sealed class RuntimeConfiguredLlmClient : ILlmClient
{
    private readonly Func<LlmConfig> _configProvider;
    private readonly LlmRouteSelector _routeSelector = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, ILlmClient> _clients = new(StringComparer.Ordinal);

    public RuntimeConfiguredLlmClient(Func<LlmConfig> configProvider)
    {
        _configProvider = configProvider;
    }

    public Task<LlmChatResponse> CompleteAsync(IEnumerable<LocalChatMessage> messages, LlmOptions? options = null, CancellationToken ct = default)
    {
        var messageList = messages.ToList();
        var route = SelectRoute(messageList, options, streaming: false);
        return GetInner(route).CompleteAsync(messageList, options, ct);
    }

    public Task<string> StreamAsync(IEnumerable<LocalChatMessage> messages, Action<string> onChunk, LlmOptions? options = null, CancellationToken ct = default)
    {
        var messageList = messages.ToList();
        var route = SelectRoute(messageList, options, streaming: true);
        return GetInner(route).StreamAsync(messageList, onChunk, options, ct);
    }

    private LlmRouteConfig SelectRoute(IReadOnlyList<LocalChatMessage> messages, LlmOptions? options, bool streaming)
    {
        var config = _configProvider();
        var requirements = LlmRequestRequirements.From(messages, options, streaming);
        var route = _routeSelector.SelectRoute(config, requirements);
        Info("llm", $"llm route selected: route={LlmRouteSelector.RouteName(route)}, provider={route.Provider}, model={route.Model}, requirements=({requirements})");
        return route;
    }

    private ILlmClient GetInner(LlmRouteConfig route)
    {
        var key = BuildCacheKey(route);

        lock (_gate)
        {
            if (_clients.TryGetValue(key, out var existing))
                return existing;

            var created = LlmClientFactory.Create(route);
            _clients[key] = created;
            return created;
        }
    }

    private static string BuildCacheKey(LlmRouteConfig route) => string.Join("|",
        LlmRouteSelector.RouteName(route),
        route.Provider,
        route.BaseUrl,
        route.ApiKey,
        route.Model,
        route.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture),
        route.MaxTokens?.ToString() ?? "null",
        route.Streaming.ToString(),
        route.TimeoutSeconds.ToString(),
        route.MaxRetryAttempts.ToString(),
        route.BaseDelayMs.ToString(),
        route.RateLimitPerMinute.ToString(),
        route.Capabilities?.SupportsVision.ToString() ?? "false",
        route.Capabilities?.SupportsJsonMode.ToString() ?? "true",
        route.Capabilities?.SupportsStreaming.ToString() ?? "true",
        route.Capabilities?.SupportsTools.ToString() ?? "false");
}
