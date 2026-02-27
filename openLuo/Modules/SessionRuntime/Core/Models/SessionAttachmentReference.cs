namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionAttachmentReference
{
    public required string AttachmentId { get; init; }

    public string? AssetId { get; init; }

    public required SessionContentKind Kind { get; init; }

    public string? Name { get; init; }

    public string? MediaType { get; init; }

    public long SizeBytes { get; init; }

    public string? Sha256 { get; init; }
}
