using openLuo.Core.Models;
using openLuo.Modules.Llm.Core.Models;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Modules.Llm.Infrastructure.Chat;

public sealed class LlmRequestRequirements
{
    public bool RequiresVision { get; init; }
    public bool RequiresJsonMode { get; init; }
    public bool RequiresStreaming { get; init; }
    public bool RequiresTools { get; init; }

    public static LlmRequestRequirements From(
        IEnumerable<LocalChatMessage> messages,
        LlmOptions? options,
        bool streaming = false)
    {
        var effectiveOptions = options ?? new LlmOptions();
        var hasImageBlocks = messages.Any(static message =>
            message.Blocks?.Any(static block => block is ImageBlock) == true);

        return new LlmRequestRequirements
        {
            RequiresVision = effectiveOptions.RequiredCapabilities.Vision
                             || (effectiveOptions.EnableMultimodal && hasImageBlocks),
            RequiresJsonMode = effectiveOptions.JsonMode || effectiveOptions.RequiredCapabilities.JsonMode,
            RequiresStreaming = streaming || effectiveOptions.RequiredCapabilities.Streaming,
            RequiresTools = effectiveOptions.Tools is { Count: > 0 }
        };
    }

    public override string ToString() =>
        $"vision={RequiresVision}, json={RequiresJsonMode}, streaming={RequiresStreaming}, tools={RequiresTools}";
}

public sealed class LlmRouteSelector
{
    public IReadOnlyList<LlmRouteConfig> BuildRoutes(LlmConfig config) =>
        config.Routes?.Select(route => route.Clone()).ToList() ?? [];

    public LlmRouteConfig SelectRoute(LlmConfig config, LlmRequestRequirements requirements)
    {
        var routes = BuildRoutes(config);
        var selected = routes
            .Where(route => route.Enabled)
            .Where(route => Supports(route, requirements))
            .OrderByDescending(route => route.Priority)
            .ThenBy(route => route.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selected is not null)
            return selected;

        var configured = string.Join(
            ", ",
            routes.Select(route =>
                $"{RouteName(route)}(enabled={route.Enabled}, vision={route.Capabilities?.SupportsVision ?? false}, json={route.Capabilities?.SupportsJsonMode ?? true}, streaming={route.Capabilities?.SupportsStreaming ?? true}, tools={route.Capabilities?.SupportsTools ?? false})"));
        throw new InvalidOperationException(
            $"No LLM route satisfies requirements: {requirements}. Configured routes: {configured}");
    }

    private static bool Supports(LlmRouteConfig route, LlmRequestRequirements requirements)
    {
        var capabilities = route.Capabilities ?? new LlmCapabilitiesConfig();
        if (requirements.RequiresVision && !capabilities.SupportsVision)
            return false;
        if (requirements.RequiresJsonMode && !capabilities.SupportsJsonMode)
            return false;
        if (requirements.RequiresStreaming && !capabilities.SupportsStreaming)
            return false;
        if (requirements.RequiresTools && !capabilities.SupportsTools)
            return false;
        return true;
    }

    public static string RouteName(LlmRouteConfig route) =>
        string.IsNullOrWhiteSpace(route.Name) ? $"{route.Provider}:{route.Model}" : route.Name;
}
