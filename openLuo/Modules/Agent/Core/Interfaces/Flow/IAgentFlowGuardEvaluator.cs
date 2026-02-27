using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Core.Interfaces.Flow;

public interface IAgentFlowGuardEvaluator
{
    bool Allows(AgentFlowGuard guard, AgentFlowRunRequest request, IReadOnlyDictionary<string, object?> state);
}
