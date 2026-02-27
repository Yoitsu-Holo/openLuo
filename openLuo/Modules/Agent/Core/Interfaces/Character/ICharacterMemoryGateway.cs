using openLuo.Modules.Agent.Application;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface ICharacterMemoryGateway
{
    Task<CharacterMemorySnapshot> LoadAsync(AgentContext context, AgentMessage message, CancellationToken ct = default);
}
