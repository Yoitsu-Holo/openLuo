using openLuo.Modules.InterAgent.Core.Models;

namespace openLuo.Modules.InterAgent.Core.Interfaces;

public interface IInterAgentMessenger
{
    Task<InterAgentAskResult> AskAsync(InterAgentAskRequest request, CancellationToken ct = default);

    Task<InterAgentChatSessionResult> ChatSessionAsync(InterAgentChatSessionRequest request, CancellationToken ct = default);
}
