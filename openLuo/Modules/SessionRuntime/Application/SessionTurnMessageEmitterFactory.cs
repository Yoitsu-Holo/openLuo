using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class SessionTurnMessageEmitterFactory(
    IOutputEventBus outputEventBus) : ITurnMessageEmitterFactory
{
    public ITurnMessageEmitter Create(AgentFlowRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) ||
            string.IsNullOrWhiteSpace(request.ChannelId))
            return NullTurnMessageEmitter.Instance;

        if (!request.Inputs.TryGetValue("presentationProfile", out var profileObj) ||
            !string.Equals(
                profileObj?.ToString(),
                SessionPresentationProfile.InstantMessageCompact.ToString(),
                StringComparison.OrdinalIgnoreCase))
            return NullTurnMessageEmitter.Instance;

        return new OutputEventBusTurnMessageEmitter(outputEventBus);
    }
}
