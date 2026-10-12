using System.Net.WebSockets;
using System.Text;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>一条在线连接：身份 + 已订阅会话（§6.1/§6.4）。</summary>
internal sealed class HubConnection
{
    private readonly HashSet<string> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private HashSet<string>? _presenceFilter;

    public required string Id { get; init; }
    public required WebSocket Socket { get; init; }
    public string? ClientId { get; set; }
    public string? ClientType { get; set; }
    public string? Role { get; set; }

    public bool Subscribed(string sessionId)
    {
        lock (_sessions)
            return _sessions.Contains(sessionId);
    }

    public bool Subscribe(string sessionId)
    {
        lock (_sessions)
            return _sessions.Add(sessionId);
    }

    public bool Unsubscribe(string sessionId)
    {
        lock (_sessions)
            return _sessions.Remove(sessionId);
    }

    public IReadOnlyList<string> Subscriptions
    {
        get { lock (_sessions) return _sessions.ToList(); }
    }

    /// <summary>设置 presence 过滤：null/空 = 收全量在场变化（原行为）；否则只收"与这些会话有交集"的连接的在场变化。</summary>
    public void SetPresenceFilter(IEnumerable<string>? sessionIds)
    {
        lock (_sessions)
            _presenceFilter = sessionIds is null ? null : new HashSet<string>(sessionIds, StringComparer.Ordinal);
    }

    /// <summary>给定连接的会话集合是否满足本连接的 presence 过滤（未设过滤 → 恒为 true）。</summary>
    public bool SeesPresenceOf(IEnumerable<string> sessions)
    {
        lock (_sessions)
            return _presenceFilter is null || _presenceFilter.Count == 0 || _presenceFilter.Overlaps(sessions);
    }

    /// <summary>
    /// 发送一条信封。**发送必须串行**：<c>ClientWebSocket</c> 不允许并发 Send；而回合流是脱离连接循环投递的
    /// （这样同一连接在回合进行中仍能处理 <c>turn.cancel</c> 等命令），故用信号量串行化。
    /// <c>Socket</c> 非 Open 时静默跳过；传输异常只记录不断连（由连接循环/回合任务清理）。
    /// </summary>
    public async Task SendAsync(Envelope envelope, CancellationToken ct = default)
    {
        if (Socket.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(envelope));
        await _sendGate.WaitAsync(ct);
        try
        {
            if (Socket.State != WebSocketState.Open)
                return;

            await Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        catch (WebSocketException)
        {
            // 断开：忽略（连接循环会清理）
        }
        finally
        {
            _sendGate.Release();
        }
    }
}
