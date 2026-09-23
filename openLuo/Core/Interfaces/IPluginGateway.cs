using System.Text.Json.Nodes;

namespace openLuo.Core.Interfaces;

/// <summary>
/// Dynamic dispatch gateway for plugin-provided capabilities.
/// 
/// All gameplay features (shop, gift, inventory, timeline, resource, asset,
/// diary, character relationships, etc.) are exposed through named routes
/// under the plugin/ prefix. The dispatch layer auto-resolves routes via
/// [GameApi]-annotated handler methods — no interface change needed when
/// plugins register new capabilities.
/// </summary>
public interface IPluginGateway
{
    /// <summary>
    /// Invoke a plugin capability by route name.
    /// Example: InvokeAsync(new PluginCallRequest { GameId = "xxx", Route = "plugin/shop/list", Params = ... })
    /// </summary>
    Task<JsonNode?> InvokeAsync(PluginCallRequest req, CancellationToken ct = default);

    /// <summary>
    /// Get all available plugin routes for discovery, help generation, and UI rendering.
    /// Only returns plugin/ routes; core/ and host/ routes are excluded.
    /// </summary>
    IReadOnlyList<RouteInfo> GetAvailableRoutes();
}

/// <summary>Request to invoke a plugin capability.</summary>
public sealed class PluginCallRequest
{
    /// <summary>Target game/save identifier.</summary>
    public required string GameId { get; init; }

    /// <summary>Route name, e.g. "plugin/shop/list".</summary>
    public required string Route { get; init; }

    /// <summary>Route parameters as JSON object.</summary>
    public JsonNode? Params { get; init; }
}

/// <summary>Metadata for a registered plugin route.</summary>
public sealed class RouteInfo
{
    /// <summary>Full route name, e.g. "plugin/shop/list".</summary>
    public required string Route { get; init; }

    /// <summary>Human-readable description from [GameApi] attribute.</summary>
    public string? Description { get; init; }

    /// <summary>Parameter metadata for this route.</summary>
    public IReadOnlyList<RouteParamInfo> Params { get; init; } = [];
}

/// <summary>Metadata for a route parameter.</summary>
public sealed class RouteParamInfo
{
    /// <summary>Parameter name.</summary>
    public required string Name { get; init; }

    /// <summary>Parameter type (e.g. "string", "int", "boolean").</summary>
    public string? Type { get; init; }

    /// <summary>Whether this parameter is required.</summary>
    public bool Required { get; init; }

    /// <summary>Default value if not supplied.</summary>
    public string? DefaultValue { get; init; }
}
