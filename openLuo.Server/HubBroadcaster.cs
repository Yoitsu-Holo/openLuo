using System.Collections.Concurrent;
using System.Net.WebSockets;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>
/// 在线连接注册与投递（§6.4）：非回合事件按**显式订阅**投递；
/// 回合事件投给「发起连接 + 该会话订阅者」；`targetClientId` 定向优先。
/// </summary>
internal sealed class HubBroadcaster
{
    private readonly ConcurrentDictionary<string, HubConnection> _connections = new(StringComparer.Ordinal);

    public HubConnection Add(WebSocket socket)
    {
        var connection = new HubConnection { Id = ProtocolIds.NewUlid(), Socket = socket };
        _connections[connection.Id] = connection;
        return connection;
    }

    public void Remove(string id) => _connections.TryRemove(id, out _);

    public IReadOnlyList<HubConnection> Connections => _connections.Values.ToList();

    /// <summary>统一投递：<paramref name="targetClientId"/> 定向；否则按会话订阅 + 显式包含的连接；会话为空则全局。</summary>
    public async Task DeliverAsync(
        Envelope envelope, string? sessionId = null, string? includeConnectionId = null,
        string? targetClientId = null, CancellationToken ct = default)
    {
        foreach (var connection in _connections.Values)
        {
            if (targetClientId is not null && !string.Equals(connection.ClientId, targetClientId, StringComparison.Ordinal))
                continue;

            var global = sessionId is null && targetClientId is null;
            var included = includeConnectionId is not null && connection.Id == includeConnectionId;
            var subscribed = sessionId is not null && connection.Subscribed(sessionId);

            if (global || included || subscribed)
                await connection.SendAsync(envelope, ct);
        }
    }

    /// <summary>仅发给已订阅在线的连接（presence 等）。</summary>
    public async Task DeliverToConnectionsAsync(IEnumerable<HubConnection> targets, Envelope envelope, CancellationToken ct = default)
    {
        foreach (var connection in targets)
            await connection.SendAsync(envelope, ct);
    }
}
