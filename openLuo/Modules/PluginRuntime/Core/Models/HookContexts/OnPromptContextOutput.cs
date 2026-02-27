using System.Text.Json.Serialization;
using openLuo.Application.Models.StateEvaluation;

namespace openLuo.Modules.PluginRuntime.Core.Models.HookContexts;

/// <summary>Output for onPromptContext hook.</summary>
public class OnPromptContextOutput
{
    [JsonPropertyName("promptFragments")]
    public List<PromptFragment>? PromptFragments { get; set; }
}
