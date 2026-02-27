using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Executor.Application.TODOList;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterTurnResult
{
    public string Reply { get; init; } = string.Empty;
    public CommandPresentation Presentation { get; init; } = CommandPresentation.Empty;
    public InterAgentOutcome? InterAgentOutcome { get; init; }
    public IReadOnlyList<AgentToolUseStep> Steps { get; init; } = [];
    public IReadOnlyList<string> VisibleBlocks { get; init; } = [];
    public AgentPendingAbility? PendingAbility { get; init; }
    public bool EndDialogue { get; init; }
    public bool ShouldContinueToolLoop { get; init; }
    public CharacterMemorySnapshot? Memory { get; init; }
    public TODOListOutput? TodoList { get; init; }
    public CharacterStateUpdateResult? StateUpdate { get; init; }
    public bool HasStreamedPublicOutput { get; init; }
}
