namespace openLuo.Modules.Content.Core.Definitions;

public sealed class ToolDefinition : ContentDefinitionBase
{
    public override ContentKind Kind => ContentKind.Tool;

    public string EntryPoint { get; set; } = string.Empty;
    public List<string> CapabilityTags { get; set; } = [];
}
