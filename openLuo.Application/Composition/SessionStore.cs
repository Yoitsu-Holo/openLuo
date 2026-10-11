using System.Collections.Concurrent;
using openLuo.AgentContext.Infrastructure;
using openLuo.Capabilities.Core;

namespace openLuo.Composition;

/// <summary>
/// 共享会话存储：<see cref="ComposedAgentRuntime"/> 与 <c>SessionContextUpdater</c> 共用同一实例；接入 HubStore 后重启可恢复。
/// </summary>
/// <remarks>
/// 除了按 <c>sessionId</c> 建索引，还维护 **(subjectId, agentId, conversationId) → sessionId** 的反向索引：
/// 这是"多端共享同一段对话"的支点（PC 客户端与手机 QQ 配置同一 conversationId 即落到同一会话）。
/// 反向索引可从 <c>sessions</c> 表零 schema 变更地重建（表内本就记录了这三个字段）。
/// 注意：会话是**寻址单位**，不是"把所有上下文塞到一起"的单位——是否共享由调用方给的 conversationId 决定；
/// 记忆/状态的所有者仍是 subjectId（后续的用户认证与公共/私有记忆划分挂在这一层，不需要改 client/hub）。
/// </remarks>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, DefaultAgentContextSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AgentSession> _meta = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (string? ClientType, string? ClientId)> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _byConversation = new(StringComparer.OrdinalIgnoreCase);
    private readonly Infrastructure.Persistence.HubStore? _store;

    public SessionStore(Infrastructure.Persistence.HubStore? store = null)
    {
        _store = store;
        if (store is null)
            return;

        // LoadSessions 按 updated_at_ms 升序返回 → 同一 (subject, agent, conversation) 多次出现时，最后写入者胜出
        // （即最近活跃的那个会话成为复用目标），与 UpsertSession 的语义一致。
        foreach (var (session, clientType, clientId, _) in store.LoadSessions())
        {
            _meta[session.SessionId] = session;
            _clients[session.SessionId] = (clientType, clientId);
            _byConversation[ConversationKey(session.SubjectId, session.AgentId, session.ConversationId)] = session.SessionId;
        }
    }

    public DefaultAgentContextSession GetOrAdd(string sessionId, Func<string, DefaultAgentContextSession> factory) =>
        _sessions.GetOrAdd(sessionId, factory);

    public DefaultAgentContextSession? Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    /// <summary>登记会话对外元数据（AgentId/ConversationId 等，协议 Get/List 用）；有存储时落盘。</summary>
    /// <remarks><paramref name="clientType"/> / <paramref name="clientId"/> 记录**最近活跃**的客户端（多端共会话时会被后来的端覆盖）。</remarks>
    public void SetMeta(AgentSession session, string? clientType = null, string? clientId = null)
    {
        _meta[session.SessionId] = session;
        if (clientType is not null || clientId is not null)
            _clients[session.SessionId] = (clientType, clientId);

        _byConversation[ConversationKey(session.SubjectId, session.AgentId, session.ConversationId)] = session.SessionId;
        _store?.UpsertSession(session, clientType, clientId, DateTimeOffset.UtcNow);
    }

    /// <summary>按 (subjectId, agentId, conversationId) 查找可复用的既有会话（无则 null）。</summary>
    public AgentSession? FindByConversation(string subjectId, string agentId, string conversationId) =>
        _byConversation.TryGetValue(ConversationKey(subjectId, agentId, conversationId), out var sessionId)
            ? GetMeta(sessionId)
            : null;

    public AgentSession? GetMeta(string sessionId) =>
        _meta.TryGetValue(sessionId, out var meta) ? meta : null;

    public IReadOnlyList<AgentSession> ListMeta() => _meta.Values.ToList();

    /// <summary>移除会话（上下文 + 元数据 + 反向索引 + 存储）。返回是否曾存在。</summary>
    public bool Remove(string sessionId)
    {
        if (_meta.TryRemove(sessionId, out var meta))
            _byConversation.TryRemove(ConversationKey(meta.SubjectId, meta.AgentId, meta.ConversationId), out _);

        var removedContext = _sessions.TryRemove(sessionId, out _);
        _clients.TryRemove(sessionId, out _);
        _store?.DeleteSession(sessionId);
        return removedContext || meta is not null;
    }

    /// <summary>反向索引键：用不可见分隔符拼接，避免字段内出现分隔符时碰撞。</summary>
    private static string ConversationKey(string subjectId, string agentId, string conversationId) =>
        $"{subjectId}\u001f{agentId}\u001f{conversationId}";
}
