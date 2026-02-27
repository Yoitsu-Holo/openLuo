using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Executor.Application.TODOList;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterTurnContext
{
    public required CharacterTurnRequest Request { get; init; }
    public required CharacterAgentProfile Profile { get; init; }
    public required CharacterAgentState State { get; init; }
    public required CharacterMemorySnapshot Memory { get; init; }
    public required string CurrentStateSummary { get; init; }
    public required AgentCapabilitySnapshot CapabilitySnapshot { get; init; }
    public required CharacterPromptContext PromptContext { get; init; }
    public TODOListOutput? TodoList { get; init; }
}
