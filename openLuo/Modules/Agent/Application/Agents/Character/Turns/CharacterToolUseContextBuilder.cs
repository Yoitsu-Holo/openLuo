using openLuo.Modules.AgentCapabilities.Core.Interfaces;
using openLuo.Modules.AgentCapabilities.Core.Models;

namespace openLuo.Modules.Agent.Application;

public interface ICharacterCapabilitySnapshotProvider
{
    Task<AgentCapabilitySnapshot> LoadAsync(CharacterTurnRequest request, CancellationToken ct = default);
}

public sealed class DefaultCharacterCapabilitySnapshotProvider : ICharacterCapabilitySnapshotProvider
{
    private readonly IAgentCapabilityRegistry _capabilityRegistry;

    public DefaultCharacterCapabilitySnapshotProvider(IAgentCapabilityRegistry capabilityRegistry)
    {
        _capabilityRegistry = capabilityRegistry;
    }

    public Task<AgentCapabilitySnapshot> LoadAsync(CharacterTurnRequest request, CancellationToken ct = default)
    {
        var capabilityContext = new AgentCapabilityContext
        {
            GameId = request.Context.GameId,
            CharacterId = request.Context.CharacterId
        };
        return _capabilityRegistry.BuildSnapshotAsync(capabilityContext, ct);
    }
}
