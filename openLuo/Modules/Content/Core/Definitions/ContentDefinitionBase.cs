using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace openLuo.Modules.Content.Core.Definitions;

public abstract class ContentDefinitionBase
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? SourcePack { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonExtensionData]
    public Dictionary<string, JsonNode?> ExtraProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public abstract ContentKind Kind { get; }
}
