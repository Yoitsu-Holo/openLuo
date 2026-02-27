using System.Text.Json.Nodes;

namespace openLuo.Modules.Content.Core.Definitions;

public sealed class PluginDefaultConfigDefinition : ContentDefinitionBase
{
    public override ContentKind Kind => ContentKind.PluginDefaultConfig;

    public string PluginId { get; set; } = string.Empty;
    public JsonObject Config { get; set; } = [];
}
