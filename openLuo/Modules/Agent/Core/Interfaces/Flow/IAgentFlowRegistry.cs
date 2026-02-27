using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Core.Interfaces.Flow;

public interface IAgentFlowRegistry
{
    void Register(AgentFlowDefinition definition);
    void Register(AgentFlowRegistration registration);
    bool TryGet(string flowId, out AgentFlowDefinition definition);
    IReadOnlyList<AgentFlowDefinition> List();
}
