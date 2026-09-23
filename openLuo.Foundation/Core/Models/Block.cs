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
    Video
}

/// <summary>
/// Origin of a content block. This is intentionally independent from chat roles.
/// </summary>
public enum BlockSource
{
    Unknown = 0,
    User = 1,
    Agent = 2
}

/// <summary>
/// A single content block — the universal content unit across the platform.
/// </summary>
public abstract class Block
{
    public required BlockKind Kind { get; init; }

    public BlockSource Source { get; init; } = BlockSource.Unknown;

    public OutputVisibility Visibility { get; init; } = OutputVisibility.Public;
}

/// <summary>
/// A text content block.
/// </summary>
public sealed class TextBlock : Block
{
    public required string Text { get; init; }

    public override string ToString() => Text;
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

    public override string ToString()
    {
        if (!string.IsNullOrWhiteSpace(DataUri))
        {
            if (DataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var commaIdx = DataUri.IndexOf(',');
                if (commaIdx >= 0 && commaIdx < DataUri.Length - 1)
                {
                    var base64Part = DataUri[(commaIdx + 1)..];
                    var sample = base64Part.Length > 20 ? string.Concat(base64Part.AsSpan(0, 20), "...") : base64Part;
                    var mime = commaIdx > 5 ? DataUri[5..commaIdx].TrimEnd(';') : "?";
                    return $"[img, base64:{sample}/{mime}]";
                }
                return $"[img, data:{DataUri[..Math.Min(80, DataUri.Length)]}]";
            }
            if (DataUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                DataUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return $"[img, url:{DataUri}]";
            return $"[img, ref:{DataUri[..Math.Min(60, DataUri.Length)]}]";
        }

        if (!string.IsNullOrWhiteSpace(AssetId))
        {
            if (AssetId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                AssetId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return $"[img, url:{AssetId}]";
            return $"[img, asset:{AssetId[..Math.Min(40, AssetId.Length)]}]";
        }

        return "[img]";
    }
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

    public override string ToString() => string.IsNullOrWhiteSpace(Name)
        ? $"[asset:{MimeType}]"
        : $"[asset:{Name}]";
}

/// <summary>
/// A video content block (MIME type starts with "video/").
/// </summary>
public sealed class VideoBlock : Block
{
    public required string AssetId { get; init; }

    public required string MimeType { get; init; }

    public string? Name { get; init; }

    public string? Caption { get; init; }

    /// <summary>
    /// Optional thumbnail as a data URI (e.g. "data:image/jpeg;base64,...").
    /// </summary>
    public string? ThumbnailDataUri { get; init; }

    /// <summary>
    /// Duration in seconds, if known.
    /// </summary>
    public double? DurationSeconds { get; init; }

    public override string ToString() => string.IsNullOrWhiteSpace(Name)
        ? $"[video, asset:{AssetId[..Math.Min(40, AssetId.Length)]}]"
        : $"[video:{Name}]";
}
