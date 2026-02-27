using openLuo.Modules.Agent.Application;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface ICharacterStateGateway
{
    Task<string> BuildStateSummaryAsync(CharacterTurnRequest request, CancellationToken ct = default);
}
