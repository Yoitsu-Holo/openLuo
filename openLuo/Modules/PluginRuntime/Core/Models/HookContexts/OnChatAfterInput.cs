using System.Text.Json.Serialization;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.PluginRuntime.Core.Models.HookContexts;

public sealed class OnChatAfterInput : HookContext, IResourceAwareHookContext
{
    [JsonPropertyName("characterId")]
    public string? CharacterId { get; set; }

    [JsonPropertyName("playerMessage")]
    public string? PlayerMessage { get; set; }

    [JsonPropertyName("finalReply")]
    public string? FinalReply { get; set; }

    [JsonPropertyName("visibleBlocks")]
    public IReadOnlyList<string> VisibleBlocks { get; set; } = [];

    [JsonPropertyName("outputBlocks")]
    public IReadOnlyList<string> OutputBlocks { get; set; } = [];

    [JsonPropertyName("traceLines")]
    public IReadOnlyList<string> TraceLines { get; set; } = [];

    [JsonPropertyName("timeSnapshot")]
    public HookTimeSnapshot? TimeSnapshot { get; set; }

    [JsonPropertyName("resourceSnapshot")]
    public IReadOnlyList<ResourceStatusItemView> ResourceSnapshot { get; set; } = [];
}
