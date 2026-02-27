namespace openLuo.Modules.PluginRuntime.Core.Models;

public sealed class GameBridgeRequestContext
{
    public string? SessionId { get; init; }

    public string? GameId { get; init; }

    public string? ClientType { get; init; }

    public string? ClientId { get; init; }

    public string? SourceId { get; init; }

    public string? ChannelId { get; init; }

    public string? ActorId { get; init; }

    public string? Reason { get; init; }
}
