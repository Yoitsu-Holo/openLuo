using System.Collections.Concurrent;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class InMemorySessionRegistry : ISessionRegistry
{
    private readonly ConcurrentDictionary<string, SessionHandle> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public SessionHandle Create(SessionOpenRequest request)
    {
        var handle = new SessionHandle
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ClientType = request.ClientType,
            ClientId = request.ClientId,
            GameId = string.IsNullOrWhiteSpace(request.PreferredGameId) ? null : request.PreferredGameId.Trim()
        };
        _sessions[handle.SessionId] = handle;
        return handle;
    }

    public SessionHandle? Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var handle) ? handle : null;

    public bool BindGameId(string sessionId, string gameId)
    {
        if (!_sessions.TryGetValue(sessionId, out var handle))
            return false;

        handle.GameId = gameId;
        return true;
    }

    public bool Exists(string sessionId) => _sessions.ContainsKey(sessionId);

    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);
}
