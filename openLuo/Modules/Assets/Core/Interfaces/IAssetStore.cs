using openLuo.Modules.Assets.Core.Models;

namespace openLuo.Modules.Assets.Core.Interfaces;

/// <summary>
/// Handles database-level read/write for asset records.
/// Manages the core asset entity lifecycle.
/// </summary>
public interface IAssetStore
{
    /// <summary>Create a new asset record. Returns the new assetId.</summary>
    Task<string> CreateAsync(AssetRecord record);

    /// <summary>Get an asset record by its ID, or null if not found.</summary>
    Task<AssetRecord?> GetByIdAsync(string assetId);

    /// <summary>
    /// Query assets with optional filters.
    /// If linkedToEntityType and linkedToEntityId are provided, only assets linked to that entity are returned.
    /// </summary>
    Task<List<AssetRecord>> QueryAsync(
        string gameId,
        string? assetType = null,
        string? @namespace = null,
        string? ownerKind = null,
        string? ownerId = null,
        string? sourceType = null,
        string? labelLike = null,
        string? linkedToEntityType = null,
        string? linkedToEntityId = null,
        int limit = 50,
        int offset = 0);

    /// <summary>Update the UpdatedAt timestamp for an asset (call after mutation).</summary>
    Task UpdateTimestampAsync(string assetId);
}
