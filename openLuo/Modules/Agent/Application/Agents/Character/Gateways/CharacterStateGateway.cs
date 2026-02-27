using openLuo.Modules.Agent.Core.Interfaces;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterStateGateway : ICharacterStateGateway
{
    public Task<string> BuildStateSummaryAsync(CharacterTurnRequest request, CancellationToken ct = default)
    {
        _ = ct;
        return Task.FromResult(request.Context.Summary);
    }
}
