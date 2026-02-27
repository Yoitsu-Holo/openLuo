namespace openLuo.Modules.Executor.Application.RandomImage;

public sealed class RandomImageFetchInput
{
    public string GameId { get; init; } = string.Empty;

    public string OwnerCharacterId { get; init; } = string.Empty;
}

public sealed class RandomImageFetchOutput
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string? AssetId { get; init; }

    public string? MimeType { get; init; }

    public string? Label { get; init; }

    public string? SourceUrl { get; init; }

    public string? FallbackArtworkUrl { get; init; }
}
