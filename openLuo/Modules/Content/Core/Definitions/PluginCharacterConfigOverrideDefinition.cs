using System.Text.Json.Nodes;

namespace openLuo.Modules.Content.Core.Definitions;

public sealed class PluginCharacterConfigOverrideDefinition : ContentDefinitionBase
{
    public override ContentKind Kind => ContentKind.PluginCharacterConfigOverride;

    public string PluginId { get; set; } = string.Empty;
    public string CharacterId { get; set; } = string.Empty;
    public JsonObject Config { get; set; } = [];
}
