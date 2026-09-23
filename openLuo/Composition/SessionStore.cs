using System.Collections.Concurrent;
using openLuo.AgentContext.Infrastructure;

namespace openLuo.Composition;

/// <summary>共享会话存储：ComposedAgentRuntime 与 SessionContextUpdater 共用同一实例。</summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, DefaultAgentContextSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public DefaultAgentContextSession GetOrAdd(string sessionId, Func<string, DefaultAgentContextSession> factory) =>
        _sessions.GetOrAdd(sessionId, factory);

    public DefaultAgentContextSession? Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;
}
