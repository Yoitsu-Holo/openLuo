using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Agent.Core.Models;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface IAgentInvocationRouter
{
    bool CanHandle(ParsedCommand parsed);

    Task<CommandResult> ExecuteAsync(AgentInvocationRequest request, CancellationToken ct = default);
}
