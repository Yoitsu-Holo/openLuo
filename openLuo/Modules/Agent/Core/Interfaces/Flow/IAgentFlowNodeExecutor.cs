using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Core.Interfaces.Flow;

public interface IAgentFlowNodeExecutor
{
    string CallName { get; }
    Task<AgentFlowNodeExecutionResult> ExecuteAsync(AgentFlowNode node, AgentFlowRunRequest request, IReadOnlyDictionary<string, object?> state, CancellationToken ct = default);
}
