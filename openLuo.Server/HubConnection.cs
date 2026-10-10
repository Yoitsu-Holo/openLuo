using System.Net.WebSockets;
using System.Text;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>一条在线连接：身份 + 已订阅会话（§6.1/§6.4）。</summary>
internal sealed class HubConnection
{
    private readonly HashSet<string> _sessions = new(StringComparer.Ordinal);

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

    /// <summary>发送一条信封。<c>Socket</c> 非 Open 时静默跳过；传输异常只记录不断连（由连接循环清理）。</summary>
    public async Task SendAsync(Envelope envelope, CancellationToken ct = default)
    {
        if (Socket.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(envelope));
        try
        {
            await Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        catch (WebSocketException)
        {
            // 断开：忽略（连接循环会清理）
        }
    }
}
