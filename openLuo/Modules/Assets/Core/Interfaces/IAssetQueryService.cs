using openLuo.Modules.Assets.Core.Models;

namespace openLuo.Modules.Assets.Core.Interfaces;

/// <summary>
/// Higher-level query service for assets.
/// Provides a unified query API that may join across multiple stores (e.g., asset records + entity links).
/// </summary>
public interface IAssetQueryService
{
    /// <summary>
    /// Query assets with optional filters.
    /// </summary>
    /// <param name="gameId">Game session ID (required).</param>
    /// <param name="assetType">Filter by asset type.</param>
    /// <param name="namespace">Filter by namespace.</param>
    /// <param name="ownerKind">Filter by owner entity kind.</param>
    /// <param name="ownerId">Filter by owner entity ID.</param>
    /// <param name="sourceType">Filter by source type ("manual", "ai_generated", "imported").</param>
    /// <param name="labelLike">Filter by label substring (LIKE match).</param>
    /// <param name="linkedToEntityType">Filter to assets linked to this entity type.</param>
    /// <param name="linkedToEntityId">Filter to assets linked to this entity ID.</param>
    /// <param name="limit">Max results to return (default 50).</param>
    /// <param name="offset">Pagination offset (default 0).</param>
    Task<List<AssetRecord>> QueryAssetsAsync(
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
}
