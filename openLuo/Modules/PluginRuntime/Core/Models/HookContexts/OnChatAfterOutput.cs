using System.Text.Json.Serialization;

namespace openLuo.Modules.PluginRuntime.Core.Models.HookContexts;

public sealed class OnChatAfterOutput
{
    [JsonPropertyName("additionalText")]
    public string? AdditionalText { get; set; }

    [JsonPropertyName("notices")]
    public List<string>? Notices { get; set; }
}
