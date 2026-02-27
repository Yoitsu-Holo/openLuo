using openLuo.Modules.Agent.Core.Models;
using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface IPlayerChatDispatcher
{
    bool CanHandle(ParsedCommand command);

    Task<CommandResult> ExecuteAsync(AgentInvocationRequest request, CancellationToken ct = default);
}
