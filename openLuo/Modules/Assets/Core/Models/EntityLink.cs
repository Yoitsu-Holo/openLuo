namespace openLuo.Modules.Assets.Core.Models;

/// <summary>
/// A directional relation between any two entities in the system.
/// Provides a generic linking mechanism across assets, characters, scenes, and other entity types.
/// </summary>
public class EntityLink
{
    /// <summary>Unique link identifier (UUID).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Entity type of the source entity (e.g., "asset", "character", "scene").</summary>
    public string FromEntityType { get; set; } = string.Empty;

    /// <summary>ID of the source entity.</summary>
    public string FromEntityId { get; set; } = string.Empty;

    /// <summary>Entity type of the target entity (e.g., "asset", "character", "scene").</summary>
    public string ToEntityType { get; set; } = string.Empty;

    /// <summary>ID of the target entity.</summary>
    public string ToEntityId { get; set; } = string.Empty;

    /// <summary>Semantic type of the link (e.g., "portrait_of", "belongs_to", "references", "derived_from").</summary>
    public string LinkType { get; set; } = string.Empty;

    /// <summary>Optional JSON metadata for link-specific attributes.</summary>
    public string? MetadataJson { get; set; }

    /// <summary>ISO 8601 creation timestamp.</summary>
    public string CreatedAt { get; set; } = string.Empty;
}
