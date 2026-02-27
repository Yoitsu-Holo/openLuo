using openLuo.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Models;

public enum GameEventKind
{
    InputAccepted,
    TextOutput,
    MessageOutput,
    Error,
    SessionState,
    SystemNotice,
    AttachmentAccepted,
    StatusSnapshot,
    AgentStep,
    TurnCompleted
}

public abstract class GameEvent
{
    public required string SessionId { get; init; }

    public required string ChannelId { get; init; }

    public required string EventId { get; init; }

    public required GameEventKind Kind { get; init; }

    public OutputVisibility Visibility { get; init; } = OutputVisibility.Public;

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class InputAcceptedEvent : GameEvent
{
    public required string RawInput { get; init; }

    public IReadOnlyList<SessionAttachmentReference> Attachments { get; init; } = [];
}

public sealed class TextOutputEvent : GameEvent
{
    public required string Text { get; init; }
}

public sealed class MessageEvent : GameEvent
{
    public required string MessageId { get; init; }

    public string SpeakerRole { get; init; } = "assistant";

    public string? SpeakerId { get; init; }

    public IReadOnlyList<Block> Blocks { get; init; } = [];
}

public sealed class ErrorEvent : GameEvent
{
    public required string Error { get; init; }
}

public sealed class SessionStateEvent : GameEvent
{
    public required string State { get; init; }

    public string? GameId { get; init; }
}

public sealed class SystemNoticeEvent : GameEvent
{
    public required string Notice { get; init; }
}

public sealed class AttachmentAcceptedEvent : GameEvent
{
    public required string AttachmentId { get; init; }

    public required SessionContentKind ContentKind { get; init; }

    public string? Name { get; init; }

    public string? MediaType { get; init; }

    public long SizeBytes { get; init; }

    public string? AssetId { get; init; }
}

public sealed class StatusSnapshotEvent : GameEvent
{
    public string? GameId { get; init; }

    public string? PlayerName { get; init; }

    public string? ArchetypeId { get; init; }

    public string? ActiveCharacterId { get; init; }

    public string? CurrentLocation { get; init; }

    public int? CurrentDay { get; init; }

    public int? CurrentMinute { get; init; }
}

public sealed class AgentStepEvent : GameEvent
{
    public required string FlowId { get; init; }

    public required string NodeId { get; init; }

    public required string NodeKind { get; init; }

    public required string CallName { get; init; }

    public string? Description { get; init; }
}

public sealed class TurnCompletedEvent : GameEvent
{
    public required string TurnId { get; init; }

    public bool Success { get; init; }

    public string? Error { get; init; }
}
