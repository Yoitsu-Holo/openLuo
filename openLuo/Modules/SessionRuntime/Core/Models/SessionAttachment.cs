namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionAttachment
{
    public required string AttachmentId { get; init; }

    public required string SessionId { get; init; }

    public required SessionContentKind Kind { get; init; }

    public string? Name { get; init; }

    public string? MediaType { get; init; }

    public string? OriginalFilePath { get; init; }

    public string? AssetId { get; init; }

    public long SizeBytes { get; init; }

    public string? Sha256 { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class SessionAttachmentPayload
{
    public required SessionAttachment Attachment { get; init; }

    public byte[] Data { get; init; } = [];
}
