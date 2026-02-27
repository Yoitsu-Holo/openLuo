using openLuo.Modules.Agent.Core.Interfaces;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterMemoryRecallNode
{
    private readonly ICharacterMemoryGateway _memoryGateway;

    public CharacterMemoryRecallNode(ICharacterMemoryGateway memoryGateway)
    {
        _memoryGateway = memoryGateway;
    }

    public Task<CharacterMemorySnapshot> ExecuteAsync(CharacterTurnRequest request, CancellationToken ct = default) =>
        _memoryGateway.LoadAsync(request.Context, request.Message, ct);
}
