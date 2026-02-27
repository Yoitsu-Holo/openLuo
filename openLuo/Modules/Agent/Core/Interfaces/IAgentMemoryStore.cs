using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface IAgentMemoryStore
{
    Task StoreAgentEventAsync(string gameId, string characterId, string content, int emotionalWeight, CancellationToken ct = default);

    Task<IReadOnlyList<MemoryRecord>> RecallSharedAsync(string query, string gameId, string? excludeCharacterId, int topK, CancellationToken ct = default);

    Task<IReadOnlyList<MemoryRecord>> GetRecentPrivateAsync(string gameId, string characterId, int count, CancellationToken ct = default);
}
