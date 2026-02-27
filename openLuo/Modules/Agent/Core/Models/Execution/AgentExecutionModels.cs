using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.AgentCapabilities.Core.Models;

namespace openLuo.Modules.Agent.Application;

public sealed class SkillDocument
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Usage { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = "low";
    public bool NeedsConfirm { get; init; }
    public string[] Capabilities { get; init; } = [];
    public string Body { get; init; } = string.Empty;
}

public sealed class AgentToolUseStep
{
    public int Iteration { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string VisibleOutput { get; init; } = string.Empty;
}

public sealed class AgentPendingAbility
{
    public required AgentCapabilityDescriptor Capability { get; init; }
    public string[] Args { get; init; } = [];
    public Dictionary<string, string> Options { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AgentToolUseResult
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
