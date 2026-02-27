using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using openLuo.Modules.PluginRuntime.Core.Models;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.PluginRuntime.Core.Models.HookContexts;

/// <summary>Input for onStatusQuery hook.</summary>
public class OnStatusQueryInput : HookContext, IResourceAwareHookContext
{
    [JsonPropertyName("characterId")]
    public string? CharacterId { get; set; }

    /// <summary>List of registered resource IDs.</summary>
    [JsonPropertyName("availableResources")]
    public List<string>? AvailableResources { get; set; }

    /// <summary>Current state values grouped by namespace: { "charStatus": { "affection": "420" }, ... }</summary>
    [JsonPropertyName("stateSnapshot")]
    public Dictionary<string, Dictionary<string, string>>? StateSnapshot { get; set; }

    /// <summary>Merged plugin configs visible to the current character/background.</summary>
    [JsonPropertyName("pluginConfigs")]
    public Dictionary<string, JsonObject>? PluginConfigs { get; set; }

    [JsonPropertyName("timeSnapshot")]
    public HookTimeSnapshot? TimeSnapshot { get; set; }

    [JsonPropertyName("resourceSnapshot")]
    public IReadOnlyList<ResourceStatusItemView> ResourceSnapshot { get; set; } = [];
}
