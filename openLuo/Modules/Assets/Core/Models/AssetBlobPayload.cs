namespace openLuo.Modules.Assets.Core.Models;

/// <summary>
/// Metadata record for a binary blob associated with an asset.
/// Note: This model does NOT contain the actual byte data — that is handled by IAssetBlobStore.
/// </summary>
public class AssetBlobPayload
{
    /// <summary>Unique blob identifier (UUID).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>ID of the parent asset record.</summary>
    public string AssetId { get; set; } = string.Empty;

    /// <summary>MIME type of the blob (e.g., "image/png", "audio/mp3").</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Role of this blob within the asset.
    /// Valid values: "primary" | "thumbnail" | "preview" | "mask" | "audio_main"
    /// </summary>
    public string BlobRole { get; set; } = string.Empty;

    /// <summary>Whether this blob is the primary representation of the asset.</summary>
    public bool IsPrimary { get; set; } = false;

    /// <summary>Size of the blob in bytes.</summary>
    public long SizeBytes { get; set; } = 0;

    /// <summary>SHA-256 hash of the blob data for integrity verification (optional).</summary>
    public string? Sha256 { get; set; }

    /// <summary>ISO 8601 creation timestamp.</summary>
    public string CreatedAt { get; set; } = string.Empty;
}
