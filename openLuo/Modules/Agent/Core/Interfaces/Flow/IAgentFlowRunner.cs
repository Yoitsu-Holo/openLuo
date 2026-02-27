using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Core.Interfaces.Flow;

public interface IAgentFlowRunner
{
    Task<AgentFlowRunResult> RunAsync(AgentFlowRunRequest request, CancellationToken ct = default);
}
