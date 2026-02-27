namespace openLuo.Modules.Assets.Core.Models;

/// <summary>
/// Tracks one unlock entitlement granted to an owner entity.
/// </summary>
public class UnlockRecord
{
    public string Id { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public string OwnerKind { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string UnlockType { get; set; } = string.Empty;
    public string UnlockedAt { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
}
