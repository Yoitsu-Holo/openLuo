namespace openLuo.Modules.Agent.Application;

public sealed class CharacterTurnRequest
{
    public required AgentContext Context { get; init; }
    public required AgentProfile Profile { get; init; }
    public required AgentMessage Message { get; init; }
    public AgentExecutionContext? ExecutionContext { get; init; }
    public required CharacterMemorySnapshot Memory { get; init; }
    public IReadOnlyList<SkillDocument> PreloadedSkills { get; init; } = [];
    public IReadOnlyList<AgentContextBlock> ExtraContexts { get; init; } = [];
}
