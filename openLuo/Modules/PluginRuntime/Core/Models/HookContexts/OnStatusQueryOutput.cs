using System.Text.Json.Serialization;
using openLuo.Application.Models.StateEvaluation;
using openLuo.Core.Interfaces;

namespace openLuo.Modules.PluginRuntime.Core.Models.HookContexts;

/// <summary>Output for onStatusQuery hook.</summary>
public class OnStatusQueryOutput
{
    /// <summary>Structured status items from this plugin (override or supplement registry defaults).</summary>
    [JsonPropertyName("statusItems")]
    public List<StatusItem>? StatusItems { get; set; }

    /// <summary>Optional prompt fragments for status context (e.g. current relationship stage description).</summary>
    [JsonPropertyName("promptFragments")]
    public List<PromptFragment>? PromptFragments { get; set; }
}
