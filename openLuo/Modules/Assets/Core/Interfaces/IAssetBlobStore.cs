using openLuo.Modules.Assets.Core.Models;

namespace openLuo.Modules.Assets.Core.Interfaces;

/// <summary>
/// Handles storage and retrieval of binary blob payloads for assets.
/// Separates blob I/O from asset record management.
/// </summary>
public interface IAssetBlobStore
{
    /// <summary>
    /// Store a blob for an asset. Returns the new blobId.
    /// </summary>
    /// <param name="assetId">Parent asset ID.</param>
    /// <param name="mimeType">MIME type of the blob (e.g., "image/png").</param>
    /// <param name="blobRole">Role of the blob: "primary" | "thumbnail" | "preview" | "mask" | "audio_main".</param>
    /// <param name="blobData">Raw binary data.</param>
    /// <param name="isPrimary">Whether this blob is the primary representation.</param>
    /// <returns>New blobId.</returns>
    Task<string> PutAsync(string assetId, string mimeType, string blobRole, byte[] blobData, bool isPrimary);

    /// <summary>Get metadata records for all blobs of an asset. Does NOT return byte data.</summary>
    Task<List<AssetBlobPayload>> GetInfoByAssetIdAsync(string assetId);

    /// <summary>Get the raw byte data for a specific blob by its ID. Returns null if not found.</summary>
    Task<byte[]?> GetDataAsync(string blobId);
}
