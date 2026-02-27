namespace openLuo.Modules.Assets.Core.Models;

/// <summary>
/// Definition of an asset type registered by a plugin.
/// Describes the category and namespace of assets that can be created under this definition.
/// AssetType and Namespace are strings (not enums) for plugin extensibility.
/// </summary>
public class AssetDef
{
    /// <summary>Asset type identifier (e.g., "image", "audio", "document", "scene_bg").</summary>
    public string AssetType { get; set; } = string.Empty;

    /// <summary>Business domain namespace (e.g., "character_portrait", "scene_illustration", "music_bgm").</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Semantic description of this asset type for UI and LLM context.</summary>
    public string? Description { get; set; }

    /// <summary>Which owner kinds are allowed for this asset type.</summary>
    public List<string> AllowedOwnerKinds { get; set; } = [];

    /// <summary>Whether blob (binary) storage is expected for this asset type.</summary>
    public bool HasBlob { get; set; } = true;

    /// <summary>Whether JSON metadata is expected for this asset type.</summary>
    public bool HasMeta { get; set; } = false;

    /// <summary>Accepted MIME types for blob payloads (empty = any).</summary>
    public List<string> AcceptedMimeTypes { get; set; } = [];

    /// <summary>Broad MIME family for this asset type (e.g., image/audio/text).</summary>
    public string? MimeFamily { get; set; }

    /// <summary>Plugin that registered this definition.</summary>
    public string? PluginId { get; set; }

    /// <summary>Extra plugin-specific metadata (JSON string).</summary>
    public string? MetadataJson { get; set; }

    /// <summary>Computed definition ID: "$AssetType:$Namespace".</summary>
    public string DefinitionId => $"{AssetType}:{Namespace}";
}
