using System.Text.Json.Serialization;

namespace openLuo.Modules.PluginRuntime.Core.Models;

/// <summary>Base class for all hook context objects.</summary>
public abstract class HookContext
{
    [JsonPropertyName("gameId")]
    public string? GameId { get; set; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>Character archetype ID for context.</summary>
    [JsonPropertyName("archetypeId")]
    public string? ArchetypeId { get; set; }

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("channelId")]
    public string? ChannelId { get; set; }

    [JsonPropertyName("actorId")]
    public string? ActorId { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("presentationProfile")]
    public string? PresentationProfile { get; set; }

    [JsonPropertyName("bridgeContext")]
    public GameBridgeRequestContext? BridgeContext { get; set; }
}
