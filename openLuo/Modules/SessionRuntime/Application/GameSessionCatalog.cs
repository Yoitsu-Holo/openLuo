using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class GameSessionCatalog(
    IGameSessionRuntime runtime,
    ISessionGameApiFactory apiFactory) : IGameSessionCatalog
{
    public Task<IReadOnlyList<SessionGameEntry>> GetGameIdsAsync(CancellationToken ct = default) =>
        runtime.GetGameIdsAsync(ct);

    public Task<IReadOnlyList<SessionArchetypeOption>> GetAvailableArchetypesAsync(CancellationToken ct = default) =>
        runtime.GetAvailableArchetypesAsync(ct);

    public async Task<IGameSession> OpenSessionAsync(SessionOpenRequest request, CancellationToken ct = default)
    {
        var handle = await runtime.OpenAsync(request, ct);
        var sessionApi = apiFactory.Create(handle);
        return new RuntimeBackedGameSession(runtime, handle, sessionApi);
    }

    public Task<IGameSession> OpenGameSessionAsync(
        string gameId,
        string clientType,
        string clientId,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        return OpenSessionAsync(new SessionOpenRequest
        {
            ClientType = clientType,
            ClientId = clientId,
            PreferredGameId = gameId,
            Metadata = metadata ?? []
        }, ct);
    }
}
