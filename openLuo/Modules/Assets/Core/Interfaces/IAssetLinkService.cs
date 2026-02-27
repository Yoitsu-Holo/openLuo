using openLuo.Modules.Assets.Core.Models;

namespace openLuo.Modules.Assets.Core.Interfaces;

/// <summary>
/// Manages directional links between entities (assets, characters, scenes, etc.).
/// Provides a generic entity relationship graph.
/// </summary>
public interface IAssetLinkService
{
    /// <summary>
    /// Create a directional link between two entities. Returns the new linkId.
    /// </summary>
    /// <param name="fromEntityType">Source entity type (e.g., "asset", "character").</param>
    /// <param name="fromEntityId">Source entity ID.</param>
    /// <param name="toEntityType">Target entity type (e.g., "asset", "scene").</param>
    /// <param name="toEntityId">Target entity ID.</param>
    /// <param name="linkType">Semantic link type (e.g., "portrait_of", "belongs_to").</param>
    /// <param name="metadataJson">Optional JSON metadata for link-specific attributes.</param>
    /// <returns>New linkId.</returns>
    Task<string> CreateLinkAsync(
        string gameId,
        string fromEntityType,
        string fromEntityId,
        string toEntityType,
        string toEntityId,
        string linkType,
        string? metadataJson = null);

    /// <summary>Get all links originating from a given entity.</summary>
    Task<List<EntityLink>> GetLinksFromAsync(string fromEntityType, string fromEntityId);

    /// <summary>Get all links pointing to a given entity.</summary>
    Task<List<EntityLink>> GetLinksToAsync(string toEntityType, string toEntityId);
}
