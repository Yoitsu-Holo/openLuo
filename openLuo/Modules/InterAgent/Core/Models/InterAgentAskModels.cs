using openLuo.Modules.Agent.Application;

namespace openLuo.Modules.InterAgent.Core.Models;

public sealed class InterAgentAskRequest
{
    public string GameId { get; init; } = string.Empty;
    public string FromCharacterId { get; init; } = string.Empty;
    public string TargetSelector { get; init; } = string.Empty;
    public string Question { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public AgentExecutionContext? ExecutionContext { get; init; }
}

public sealed class InterAgentAskResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
    public string TargetCharacterId { get; init; } = string.Empty;
    public string TargetDisplayName { get; init; } = string.Empty;
    public string Reply { get; init; } = string.Empty;
    public InterAgentOutcome? Outcome { get; init; }
}

public sealed class InterAgentChatSessionRequest
{
    public string GameId { get; init; } = string.Empty;
    public string FromCharacterId { get; init; } = string.Empty;
    public string TargetSelector { get; init; } = string.Empty;
    public string Opening { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public AgentExecutionContext? ExecutionContext { get; init; }
}

public sealed class InterAgentDialogueTurn
{
    public string SpeakerCharacterId { get; init; } = string.Empty;
    public string SpeakerDisplayName { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

public sealed class InterAgentChatSessionResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
    public string TargetCharacterId { get; init; } = string.Empty;
    public string TargetDisplayName { get; init; } = string.Empty;
    public IReadOnlyList<InterAgentDialogueTurn> Transcript { get; init; } = [];
}
