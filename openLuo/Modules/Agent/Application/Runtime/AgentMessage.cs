using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.Agent.Application;

public enum AgentMessageType
{
    Chat,
    AgentAsk,
    AgentReply,
    AgentDialogueTurn,
    TaskAssign,
    TaskResult,
    ToolResult,
    Presence,
    System
}

public sealed record AgentMessage(
    string MessageId,
    string GameId,
    string From,
    string To,
    AgentMessageType Type,
    string Payload,
    string? CorrelationId,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<string>? TraceLines = null,
    IReadOnlyList<string>? VisibleBlocks = null,
    CommandPresentation? Presentation = null,
    IReadOnlyList<Block>? Blocks = null,
    bool EndDialogue = false,
    AgentPendingAbility? PendingAbility = null,
    AgentExecutionContext? ExecutionContext = null,
    IReadOnlyList<AgentContextBlock>? ContextBlocks = null,
    InterAgentOutcome? InterAgentOutcome = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed class AgentDispatchItem
{
    public required AgentMessage Message { get; init; }

    public TaskCompletionSource<AgentMessage?>? ReplySink { get; init; }

    public CancellationToken HandlingToken { get; init; }
}
