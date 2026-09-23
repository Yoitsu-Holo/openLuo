using System.Text.Json.Nodes;
using openLuo.Core.Models;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using static openLuo.Infrastructure.Logging.Logger;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Modules.Llm.Infrastructure.Chat;

/// <summary>路由决策结果：选定 route + 主请求是否启用多模态（图是否真正上线）。</summary>
public sealed record LlmRouteDecision(LlmRouteConfig Route, bool EnableMultimodal);

/// <summary>
/// 模型路由决策器（D60）。
/// 语义：路由只回答"这次主回复是否需要视觉能力"，模型选择回归系统能力匹配。
/// - 候选（满足硬需求：json/streaming/tools/显式 vision）= 0 → 抛
/// - 候选 = 1 → 短路返回
/// - 调用方显式要求 vision → 直接选视觉候选，不做推理
/// - 上下文无图 → 不需要识图 → 非视觉候选优先（零路由推理）
/// - 上下文有图 → 用文本路由模型做一次微型推理，prompt 只含图片 base64 采样
///   （让模型"意识到有图"，不读内容），返回 {"multimodal": true|false}；
///   失败/超时/解析失败 → 回退：有图 → 视觉候选
///
/// 路由推理直连 LlmClientFactory（绕过 RuntimeConfiguredLlmClient，天然防递归）。
/// </summary>
public sealed class LlmRouteDecider
{
    private readonly Func<LlmConfig> _configProvider;
    private readonly Func<LlmRouteConfig, ILlmClient> _clientFactory;
    private readonly LlmRouteSelector _routeSelector = new();

    public LlmRouteDecider(Func<LlmConfig> configProvider, Func<LlmRouteConfig, ILlmClient>? clientFactory = null)
    {
        _configProvider = configProvider;
        _clientFactory = clientFactory ?? LlmClientFactory.Create;
    }

    public async Task<LlmRouteDecision> DecideAsync(
        IReadOnlyList<LocalChatMessage> messages,
        LlmOptions? options,
        bool streaming,
        CancellationToken ct = default)
    {
        var config = _configProvider();
        var requirements = LlmRequestRequirements.From(messages, options, streaming);
        var candidates = _routeSelector.MatchRoutes(config, requirements);

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"No LLM route satisfies requirements: {requirements}. Configured routes: {Describe(config)}");

        var explicitVision = options?.RequiredCapabilities?.Vision == true;
        var hasImage = messages.Any(m => m.Blocks?.Any(b => b is ImageBlock) == true);

        // 候选唯一：无选择空间，直接短路（零路由推理）
        if (candidates.Count == 1)
            return new LlmRouteDecision(candidates[0], hasImage && candidates[0].SupportsVision());

        // 显式要求视觉：无歧义，直接选视觉候选
        if (explicitVision)
        {
            var vision = FirstVision(candidates);
            return new LlmRouteDecision(vision ?? candidates[0], true);
        }

        // 上下文无图：不需要识图 → 非视觉候选优先
        if (!hasImage)
        {
            var nonVision = FirstNonVision(candidates);
            return new LlmRouteDecision(nonVision ?? candidates[0], false);
        }

        // 上下文有图：问路由模型"这次回复是否需要看图"
        var multimodal = await AskRouterAsync(config, requirements, candidates, messages, ct);
        if (multimodal is not null)
        {
            Info("llm", $"llm route decision: multimodal={multimodal} (chosen by llm router)");
            if (multimodal == true)
            {
                var vision = FirstVision(candidates);
                return new LlmRouteDecision(vision ?? candidates[0], true);
            }

            // multimodal=false：调用方 EnableMultimodal=true（上下文有图）会把文本模型排除在
            // 候选外——路由否决识图后，以"禁用多模态"视角重算候选，让文本模型可入选。
            var textRequirements = new LlmRequestRequirements
            {
                RequiresVision = false,
                RequiresJsonMode = requirements.RequiresJsonMode,
                RequiresStreaming = requirements.RequiresStreaming,
                RequiresTools = requirements.RequiresTools
            };
            var textCandidates = _routeSelector.MatchRoutes(config, textRequirements);
            var textRoute = FirstNonVision(textCandidates) ?? textCandidates.FirstOrDefault();
            return new LlmRouteDecision(textRoute ?? candidates[0], false);
        }

        // 路由推理不可用：有图回退视觉优先（宁可多花一次视觉，不可答非所图）
        Info("llm", "llm route decision unavailable, falling back to rule route");
        var fallbackVision = FirstVision(candidates);
        return new LlmRouteDecision(fallbackVision ?? candidates[0], fallbackVision is not null);
    }

    private async Task<bool?> AskRouterAsync(
        LlmConfig config,
        LlmRequestRequirements requirements,
        IReadOnlyList<LlmRouteConfig> candidates,
        IReadOnlyList<LocalChatMessage> messages,
        CancellationToken ct)
    {
        var routing = config.Routing ?? new LlmRoutingConfig();
        var routerRoute = config.Routes.FirstOrDefault(route =>
                              route.Enabled && string.Equals(route.Name, routing.Model, StringComparison.OrdinalIgnoreCase))
                          ?? candidates[0];

        var latestUserText = messages.LastOrDefault(m => m.Role == ChatMessageRole.User)?.Content ?? "(no text)";
        var imageSamples = CollectImageSamples(messages);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, routing.TimeoutSeconds)));
            // 覆盖路由推理自身的超时/重试，避免路由器卡住主链路
            var routerClientConfig = routerRoute.Clone();
            routerClientConfig.TimeoutSeconds = Math.Max(1, (int)Math.Ceiling(routing.TimeoutSeconds));
            routerClientConfig.MaxRetryAttempts = Math.Min(1, routerClientConfig.MaxRetryAttempts);

            var response = await _clientFactory(routerClientConfig)
                .CompleteAsync(
                    [new LocalChatMessage(ChatMessageRole.User, BuildRouterPrompt(latestUserText, imageSamples))],
                    new LlmOptions
                    {
                        Temperature = 0f,
                        MaxTokens = Math.Clamp(routing.MaxTokens, 16, 512),
                        JsonMode = true
                    },
                    cts.Token);

            return TryParseMultimodal(response.Content);
        }
        catch (Exception ex)
        {
            Error("llm", $"llm route decision failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>收集上下文里最近的图片采样（base64 片段，仅作"有图"信号，非完整内容）。</summary>
    private static List<string> CollectImageSamples(IReadOnlyList<LocalChatMessage> messages)
    {
        var samples = new List<string>();
        foreach (var message in messages)
        {
            if (message.Blocks is null)
                continue;
            foreach (var block in message.Blocks)
            {
                if (block is not ImageBlock image)
                    continue;
                var raw = !string.IsNullOrWhiteSpace(image.DataUri) ? image.DataUri : image.AssetId;
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var comma = raw.IndexOf(',');
                var payload = comma >= 0 ? raw[(comma + 1)..] : raw;
                var sample = payload.Length > 48 ? payload[..48] + "..." : payload;
                samples.Add($"[image:base64: {sample}]");
            }
        }
        return samples.Count > 3 ? samples.TakeLast(3).ToList() : samples;
    }

    /// <summary>解析 {"multimodal": true|false}；容错纯文本 true/false。</summary>
    private static bool? TryParseMultimodal(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        // 剥离 markdown code fence
        var normalized = content.Trim();
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            var fenceEnd = normalized.IndexOf('\n');
            if (fenceEnd > 0)
                normalized = normalized[(fenceEnd + 1)..];
            if (normalized.EndsWith("```", StringComparison.Ordinal))
                normalized = normalized[..^3];
            normalized = normalized.Trim();
        }

        try
        {
            var node = JsonNode.Parse(normalized);
            if (node?["multimodal"] is { } value)
            {
                if (value is JsonValue)
                {
                    var raw = value.ToJsonString().Trim('"').ToLowerInvariant();
                    if (raw is "true" or "1") return true;
                    if (raw is "false" or "0") return false;
                }
            }
        }
        catch
        {
            // 落入纯文本
        }

        var text = normalized.Trim().Trim('"', '\'', ' ', '\n', '\r', '`', '.').ToLowerInvariant();
        if (text is "true" or "yes" or "需要") return true;
        if (text is "false" or "no" or "不需要") return false;
        return null;
    }

    public static string BuildRouterPrompt(string latestUserText, IReadOnlyList<string> imageSamples)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a model router for a companion chat system. Decide whether the next user reply needs vision (image understanding).");
        sb.AppendLine();
        sb.AppendLine($"User message: {latestUserText}");
        sb.AppendLine(imageSamples.Count > 0
            ? $"Context images: {string.Join(" ", imageSamples)}"
            : "Context images: (none)");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- The user asks about or wants analysis of an attached image → true.");
        sb.AppendLine("- The image is decoration or unrelated to the question (e.g. someone else's image in a group chat) → false.");
        sb.AppendLine("- The question refers to an image (this/it/look/see/那/这个/它) and context images exist → true.");
        sb.AppendLine("- Plain chat with no image dependency → false.");
        sb.AppendLine();
        sb.AppendLine("Reply with JSON only: {\"multimodal\": true|false}");
        return sb.ToString();
    }

    private static LlmRouteConfig? FirstVision(IReadOnlyList<LlmRouteConfig> candidates) =>
        candidates.FirstOrDefault(c => c.SupportsVision());

    private static LlmRouteConfig? FirstNonVision(IReadOnlyList<LlmRouteConfig> candidates) =>
        candidates.FirstOrDefault(c => !c.SupportsVision());

    private static string Describe(LlmConfig config) => string.Join(", ",
        config.Routes.Select(route =>
            $"{LlmRouteSelector.RouteName(route)}(enabled={route.Enabled}, vision={route.Capabilities?.SupportsVision ?? false}, json={route.Capabilities?.SupportsJsonMode ?? true}, streaming={route.Capabilities?.SupportsStreaming ?? true}, tools={route.Capabilities?.SupportsTools ?? false})"));
}

internal static class LlmRouteConfigCapabilityExtensions
{
    public static bool SupportsVision(this LlmRouteConfig route) =>
        route.Capabilities?.SupportsVision ?? false;
}
