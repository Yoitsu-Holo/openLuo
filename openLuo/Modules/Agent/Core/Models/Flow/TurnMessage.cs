using openLuo.Core.Models;

namespace openLuo.Modules.Agent.Core.Models.Flow;

public enum TurnMessageKind
{
    Message,
    Completed
}

public sealed class TurnMessage
{
    public required string TurnId { get; init; }

    public required string SessionId { get; init; }

    public required string ChannelId { get; init; }

    public required string GameId { get; init; }

    public required string NodeId { get; init; }

    public TurnMessageKind Kind { get; init; } = TurnMessageKind.Message;

    public Message Message { get; init; } = Message.Empty;

    public bool Success { get; init; } = true;

    public string? Error { get; init; }
}
