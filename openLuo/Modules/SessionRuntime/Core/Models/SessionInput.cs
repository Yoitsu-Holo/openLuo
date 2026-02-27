namespace openLuo.Modules.SessionRuntime.Core.Models;

public enum SessionInputKind
{
    Text,
    Chat,
    Command,
    Ambient,
    System,
    Selection,
    Confirm
}

public enum SessionPresentationProfile
{
    Default,
    Narrative,
    InstantMessageCompact,
    RichDesktop
}

public enum SessionContentKind
{
    Text,
    Binary,
    FileReference
}

public sealed class SessionInputPart
{
    public required SessionContentKind Kind { get; init; }

    public string? Name { get; init; }

    public string? MediaType { get; init; }

    public string? Text { get; init; }

    public byte[]? Data { get; init; }

    public string? FilePath { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];
}

public sealed class SessionCommandInvocation
{
    public required string Name { get; init; }

    public char Prefix { get; init; } = '/';

    public string RawText { get; init; } = string.Empty;

    public IReadOnlyList<string> Args { get; init; } = [];

    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class SessionInputOrigin
{
    public string Platform { get; init; } = string.Empty;

    public string Scene { get; init; } = string.Empty;

    public string? ConversationId { get; init; }

    public string? UserId { get; init; }

    public string? UserDisplayName { get; init; }

    public bool MentionedAgent { get; init; }

    public bool IsDirectMessage { get; init; }
}

public sealed class SessionInput
{
    public required string SessionId { get; init; }

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
