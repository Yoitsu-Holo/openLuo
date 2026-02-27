namespace openLuo.Modules.Content.Core.Definitions;

public sealed class ResourceDefinition : ContentDefinitionBase
{
    public override ContentKind Kind => ContentKind.Resource;

    public string ResourceType { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal? InitialValue { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public List<string> Tags { get; set; } = [];
}
