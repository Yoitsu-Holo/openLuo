namespace openLuo.Modules.Assets.Core.Models;

/// <summary>
/// An asset entity record — represents a single asset instance in the system.
/// Each record tracks ownership, type, status, and provenance.
/// </summary>
public class AssetRecord
{
    /// <summary>Unique asset identifier (UUID).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Game session this asset belongs to.</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>Asset type (e.g., "image", "audio", "document").</summary>
    public string AssetType { get; set; } = string.Empty;

    /// <summary>Business domain namespace (e.g., "character_portrait", "scene_illustration").</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>
    /// Kind of entity that owns this asset.
    /// Valid values: "game" | "character" | "scene" | "object" | "system" | "event"
    /// </summary>
    public string OwnerKind { get; set; } = string.Empty;

    /// <summary>ID of the owning entity (e.g., character ID, scene ID).</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Optional human-readable label for the asset.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Asset lifecycle status.
    /// Valid values: "active" | "archived" | "deleted" | "pending"
    /// </summary>
    public string Status { get; set; } = "active";

    /// <summary>
    /// How this asset was created.
    /// Valid values: "manual" | "ai_generated" | "imported"
    /// </summary>
    public string SourceType { get; set; } = "manual";

    /// <summary>ISO 8601 creation timestamp.</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>ISO 8601 last-updated timestamp.</summary>
    public string UpdatedAt { get; set; } = string.Empty;
}
