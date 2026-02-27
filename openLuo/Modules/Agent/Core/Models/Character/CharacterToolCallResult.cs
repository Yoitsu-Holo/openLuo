using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterToolCallResult
{
    public string Reply { get; init; } = string.Empty;
    public InterAgentOutcome? InterAgentOutcome { get; init; }
    public IReadOnlyList<AgentToolUseStep> Steps { get; init; } = [];
    public IReadOnlyList<string> VisibleBlocks { get; init; } = [];
    public CommandPresentation Presentation { get; init; } = CommandPresentation.Empty;
    public AgentPendingAbility? PendingAbility { get; init; }
    public bool EndDialogue { get; init; }
    public bool ShouldContinueToolLoop { get; init; }
}
