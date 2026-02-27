namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionExecutionContext
{
    public required string SessionId { get; init; }

    public string? GameId { get; init; }

    public required string SourceId { get; init; }

    public required string ChannelId { get; init; }

    public required string ActorId { get; init; }

    public IReadOnlyList<SessionAttachmentReference> Attachments { get; init; } = [];

    public SessionInputKind InputKind { get; init; } = SessionInputKind.Text;

    public SessionInputOrigin? Origin { get; init; }

    public SessionPresentationProfile PresentationProfile { get; init; } = SessionPresentationProfile.Default;

    public Dictionary<string, string> Metadata { get; init; } = [];
}
