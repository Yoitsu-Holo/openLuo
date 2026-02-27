namespace openLuo.Modules.GameBridge.Core.Attributes;

/// <summary>
/// Marks a method as a Game API endpoint that can be called from both
/// frontend C# code (direct invocation) and backend plugins (JSON-RPC dispatch).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class GameApiAttribute : Attribute
{
    /// <summary>The JSON-RPC method route, e.g. "game/state/get".</summary>
    public string Route { get; }

    /// <summary>Human-readable description for help / documentation generation.</summary>
    public string? Description { get; init; }

    public GameApiAttribute(string route)
    {
        Route = route;
    }
}
