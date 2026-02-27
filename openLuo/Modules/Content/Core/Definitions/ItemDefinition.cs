using System.Text.Json.Serialization;

namespace openLuo.Modules.Content.Core.Definitions;

public sealed class ItemDefinition : ContentDefinitionBase
{
    public override ContentKind Kind => ContentKind.Item;

    [JsonPropertyName("name")]
    public string LegacyName
    {
        get => DisplayName;
        set => DisplayName = value ?? string.Empty;
    }

    public int Price { get; set; }
    public string Rarity { get; set; } = "Common";
    public string[] Tags { get; set; } = [];
    public int AffectionDelta { get; set; }
    public string? MoodEffect { get; set; }
}
