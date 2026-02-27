using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

public interface IGameSessionCatalog
{
    Task<IReadOnlyList<SessionGameEntry>> GetGameIdsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SessionArchetypeOption>> GetAvailableArchetypesAsync(CancellationToken ct = default);

    Task<IGameSession> OpenSessionAsync(SessionOpenRequest request, CancellationToken ct = default);

    Task<IGameSession> OpenGameSessionAsync(
        string gameId,
        string clientType,
        string clientId,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);
}
