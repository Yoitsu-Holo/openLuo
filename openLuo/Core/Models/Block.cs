namespace openLuo.Core.Models;

/// <summary>
/// Visibility level for output blocks and messages.
/// </summary>
public enum OutputVisibility
{
    Public,
    StateSummary,
    System,
    Debug
}

/// <summary>
/// Kind of a content block. Used for polymorphic dispatch.
/// </summary>
public enum BlockKind
{
    Text,
    Image,
    Asset,
    Audio
}

/// <summary>
/// A single content block — the universal content unit across the platform.
/// </summary>
public abstract class Block
{
    public required BlockKind Kind { get; init; }

    public OutputVisibility Visibility { get; init; } = OutputVisibility.Public;
}

/// <summary>
/// A text content block.
/// </summary>
public sealed class TextBlock : Block
{
    public required string Text { get; init; }
}

/// <summary>
/// An image content block (MIME type starts with "image/").
/// </summary>
public sealed class ImageBlock : Block
{
    public required string AssetId { get; init; }

    public required string MimeType { get; init; }

    public string? Name { get; init; }

    public string? AltText { get; init; }

    public string? Caption { get; init; }

    public string? RenderHint { get; init; }

    /// <summary>
    /// Resolved data URI (e.g. "data:image/jpeg;base64,...") for direct LLM consumption.
    /// Set by the image resolution pipeline before passing blocks to the LLM provider.
    /// </summary>
    public string? DataUri { get; init; }
}

/// <summary>
/// A generic asset content block (non-image MIME types).
/// </summary>
public sealed class AssetBlock : Block
{
    public required string AssetId { get; init; }

    public required string MimeType { get; init; }

    public string BlobRole { get; init; } = "primary";

    public string? Name { get; init; }
}
