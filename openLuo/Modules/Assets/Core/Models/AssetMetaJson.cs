namespace openLuo.Modules.Assets.Core.Models;

/// <summary>
/// JSON metadata record attached to an asset.
/// Supports multiple metadata entries per asset, each with a typed role.
/// </summary>
public class AssetMetaJson
{
    /// <summary>Unique metadata record identifier (UUID).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>ID of the parent asset record.</summary>
    public string AssetId { get; set; } = string.Empty;

    /// <summary>
    /// Type/role of this metadata entry.
    /// Valid values: "generation" | "workflow" | "prompt" | "extra" | "caption" | "ui"
    /// </summary>
    public string MetaType { get; set; } = string.Empty;

    /// <summary>JSON payload string containing the metadata content.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>ISO 8601 creation timestamp.</summary>
    public string CreatedAt { get; set; } = string.Empty;
}
