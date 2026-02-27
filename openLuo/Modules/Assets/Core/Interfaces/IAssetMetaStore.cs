using openLuo.Modules.Assets.Core.Models;

namespace openLuo.Modules.Assets.Core.Interfaces;

/// <summary>
/// Handles storage and retrieval of JSON metadata records for assets.
/// Multiple metadata entries per asset are supported, distinguished by MetaType.
/// </summary>
public interface IAssetMetaStore
{
    /// <summary>
    /// Store a JSON metadata entry for an asset. Returns the new metaId.
    /// </summary>
    /// <param name="assetId">Parent asset ID.</param>
    /// <param name="metaType">Type/role: "generation" | "workflow" | "prompt" | "extra" | "caption" | "ui".</param>
    /// <param name="payloadJson">JSON payload string.</param>
    /// <returns>New metaId.</returns>
    Task<string> PutAsync(string assetId, string metaType, string payloadJson);

    /// <summary>Get all metadata records for a given asset.</summary>
    Task<List<AssetMetaJson>> GetByAssetIdAsync(string assetId);
}
