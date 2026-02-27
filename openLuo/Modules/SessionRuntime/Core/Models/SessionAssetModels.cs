namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionAssetDescriptor
{
    public required string AssetId { get; init; }

    public required string AssetType { get; init; }

    public required string Namespace { get; init; }

    public string? Label { get; init; }

    public IReadOnlyList<SessionAssetBlobInfo> BlobInfos { get; init; } = [];
}

public sealed class SessionAssetBlobInfo
{
    public required string BlobId { get; init; }

    public required string MimeType { get; init; }

    public required string BlobRole { get; init; }

    public bool IsPrimary { get; init; }

    public long SizeBytes { get; init; }

    public string? Sha256 { get; init; }
}

public sealed class SessionAssetBlob
{
    public required string AssetId { get; init; }

    public required string BlobId { get; init; }

    public required string MimeType { get; init; }

    public required byte[] Data { get; init; }
}
