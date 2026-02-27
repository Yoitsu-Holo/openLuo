using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Core.Interfaces.Flow;

public interface ITurnMessageEmitter
{
    bool HasPublishedPublicMessage { get; }

    Task PublishAsync(TurnMessage message, CancellationToken ct = default);
}

public interface ITurnMessageEmitterFactory
{
    ITurnMessageEmitter Create(AgentFlowRunRequest request);
}
