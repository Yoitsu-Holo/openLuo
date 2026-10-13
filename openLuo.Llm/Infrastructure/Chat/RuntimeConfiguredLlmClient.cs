using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using static openLuo.Infrastructure.Logging.Logger;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Modules.Llm.Infrastructure.Chat;

public sealed class RuntimeConfiguredLlmClient : ILlmClient
{
    private readonly Func<LlmConfig> _configProvider;
    private readonly LlmRouteDecider _routeDecider;
    private readonly object _gate = new();
    private readonly Dictionary<string, ILlmClient> _clients = new(StringComparer.Ordinal);

    public RuntimeConfiguredLlmClient(Func<LlmConfig> configProvider)
    {
        _configProvider = configProvider;
        _routeDecider = new LlmRouteDecider(configProvider);
    }

    public async Task<LlmChatResponse> CompleteAsync(IEnumerable<LocalChatMessage> messages, LlmOptions? options = null, CancellationToken ct = default)
    {
        var messageList = messages.ToList();
        var decision = await SelectRouteAsync(messageList, options, streaming: false, ct);
        // 路由决策反哺主请求：判"不需要识图"时强制关闭多模态（图块不上线），
        // 判"需要"时确保开启（即使调用方未显式要求）。
        // 调用方未传 options 时补一个空 options：其 Temperature/MaxTokens 默认是 null（= 未指定），
        // 由内层 LlmClientBase.MergeOptions 回落到 route 配置；若这里换成带默认值的对象，route 配置会被遮蔽。
        var effectiveOptions = options ?? new LlmOptions();
        if (effectiveOptions.EnableMultimodal != decision.EnableMultimodal)
            effectiveOptions.EnableMultimodal = decision.EnableMultimodal;
        return await GetInner(decision.Route).CompleteAsync(messageList, effectiveOptions, ct);
    }

    public async Task<string> StreamAsync(IEnumerable<LocalChatMessage> messages, Action<string> onChunk, LlmOptions? options = null, CancellationToken ct = default)
    {
        var messageList = messages.ToList();
        var decision = await SelectRouteAsync(messageList, options, streaming: true, ct);
        var effectiveOptions = options ?? new LlmOptions();
        if (effectiveOptions.EnableMultimodal != decision.EnableMultimodal)
            effectiveOptions.EnableMultimodal = decision.EnableMultimodal;
        return await GetInner(decision.Route).StreamAsync(messageList, onChunk, effectiveOptions, ct);
    }

    private async Task<LlmRouteDecision> SelectRouteAsync(IReadOnlyList<LocalChatMessage> messages, LlmOptions? options, bool streaming, CancellationToken ct)
    {
        var config = _configProvider();
        var requirements = LlmRequestRequirements.From(messages, options, streaming);
        var decision = await _routeDecider.DecideAsync(messages, options, streaming, ct);
        Info("llm", $"llm route selected: route={LlmRouteSelector.RouteName(decision.Route)}, provider={decision.Route.Provider}, model={decision.Route.Model}, requirements=({requirements}), multimodal={decision.EnableMultimodal}");
        return decision;
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
