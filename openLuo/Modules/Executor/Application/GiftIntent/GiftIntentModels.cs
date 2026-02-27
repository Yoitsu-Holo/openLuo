using System.Text.Json.Serialization;

namespace openLuo.Modules.Executor.Application.GiftIntent;

public sealed class GiftIntentInput
{
    public string TargetCharacterName { get; init; } = string.Empty;
    public string PlayerInput { get; init; } = string.Empty;
    public IReadOnlyList<GiftIntentInventoryItem> InventoryItems { get; init; } = [];
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public sealed class GiftIntentInventoryItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

public sealed class GiftIntentOutput
{
    [JsonPropertyName("hasGiftIntent")]
    public bool HasGiftIntent { get; init; }

    [JsonPropertyName("itemRef")]
    public string ItemRef { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}
