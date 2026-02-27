using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.AgentCapabilities.Core.Interfaces;

public interface IAgentCapabilityRegistry
{
    Task<AgentCapabilitySnapshot> BuildSnapshotAsync(AgentCapabilityContext context, CancellationToken ct = default);
}

public interface IAgentCapabilityExecutor
{
    Task<CommandResult> ExecuteAsync(
        AgentCapabilityDescriptor capability,
        string[] args,
        Dictionary<string, string> options,
        AgentCapabilityContext context,
        CancellationToken ct = default);
}
