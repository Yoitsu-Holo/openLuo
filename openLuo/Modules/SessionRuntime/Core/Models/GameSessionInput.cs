namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class GameSessionInput
{
    public required string SourceId { get; init; }

    public required string ChannelId { get; init; }

    public required string ActorId { get; init; }

    public required SessionInputKind Kind { get; init; }

    public string? Text { get; init; }

    public SessionCommandInvocation? Command { get; init; }

    public IReadOnlyList<SessionInputPart> Parts { get; init; } = [];

    public SessionInputOrigin? Origin { get; init; }

    public SessionPresentationProfile PresentationProfile { get; init; } = SessionPresentationProfile.Default;

    public Dictionary<string, string> Arguments { get; init; } = [];

    public Dictionary<string, string> Metadata { get; init; } = [];

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
