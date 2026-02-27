using openLuo.Modules.Agent.Application;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface ICharacterAgent
{
    Task<CharacterTurnResult> RunTurnAsync(CharacterTurnRequest request, CancellationToken ct = default);
}
