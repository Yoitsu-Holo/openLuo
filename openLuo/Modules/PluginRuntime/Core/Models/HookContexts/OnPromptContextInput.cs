using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using openLuo.Modules.PluginRuntime.Core.Models;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.PluginRuntime.Core.Models.HookContexts;

/// <summary>Input for onPromptContext hook.</summary>
public class OnPromptContextInput : HookContext, IResourceAwareHookContext
{
    /// <summary>"chat-before" or "chat-evaluate"</summary>
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = string.Empty;

    [JsonPropertyName("characterId")]
    public string? CharacterId { get; set; }

    /// <summary>Interaction type: "chat", "date", "sleep", etc.</summary>
    [JsonPropertyName("interactionType")]
    public string? InteractionType { get; set; }

    [JsonPropertyName("playerMessage")]
    public string? PlayerMessage { get; set; }

    [JsonPropertyName("beatSummary")]
    public string? BeatSummary { get; set; }

    [JsonPropertyName("moodSignals")]
    public List<string>? MoodSignals { get; set; }

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
