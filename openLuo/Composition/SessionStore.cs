using System.Collections.Concurrent;
using openLuo.AgentContext.Infrastructure;
using openLuo.Capabilities.Core;

namespace openLuo.Composition;

/// <summary>共享会话存储：ComposedAgentRuntime 与 SessionContextUpdater 共用同一实例。</summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, DefaultAgentContextSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AgentSession> _meta = new(StringComparer.OrdinalIgnoreCase);

    public DefaultAgentContextSession GetOrAdd(string sessionId, Func<string, DefaultAgentContextSession> factory) =>
        _sessions.GetOrAdd(sessionId, factory);

    public DefaultAgentContextSession? Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    /// <summary>登记会话对外元数据（AgentId/ConversationId 等，协议 Get/List 用）。</summary>
    public void SetMeta(AgentSession session) => _meta[session.SessionId] = session;

    public AgentSession? GetMeta(string sessionId) =>
        _meta.TryGetValue(sessionId, out var meta) ? meta : null;

    public IReadOnlyList<AgentSession> ListMeta() => _meta.Values.ToList();

    /// <summary>移除会话（上下文 + 元数据）。返回是否曾存在。</summary>
    public bool Remove(string sessionId)
    {
        var removedContext = _sessions.TryRemove(sessionId, out _);
        var removedMeta = _meta.TryRemove(sessionId, out _);
        return removedContext || removedMeta;
    }
}
