using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterMemoryRecallFlowNodeExecutor : IAgentFlowNodeExecutor
{
    public string CallName => "character.memory_recall";

    private readonly CharacterMemoryRecallNode _memoryNode;

    public CharacterMemoryRecallFlowNodeExecutor(CharacterMemoryRecallNode memoryNode)
    {
        _memoryNode = memoryNode;
    }

    public async Task<AgentFlowNodeExecutionResult> ExecuteAsync(
        AgentFlowNode node,
        AgentFlowRunRequest request,
        IReadOnlyDictionary<string, object?> state,
        CancellationToken ct = default)
    {
        if (!CharacterFlowExecutionHelpers.TryGetTurnContext(state, out var turnContext))
            return AgentFlowNodeExecutionResult.Fail("Missing turnContext in flow state.");

        var memory = await _memoryNode.ExecuteAsync(turnContext.Request, ct);
        var updatedContext = CharacterFlowExecutionHelpers.CloneTurnContext(turnContext, memory: memory);

        return AgentFlowNodeExecutionResult.Ok(
            memory,
            new Dictionary<string, object?>
            {
                ["memory"] = memory,
                ["turnContext"] = updatedContext
            });
    }
}
