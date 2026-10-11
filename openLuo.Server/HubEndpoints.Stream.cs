using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：WebSocket 数据面入口，§6）。</summary>
public static partial class HubServer
{
    /// <summary>
    /// 数据面入口 `GET /v1/stream`：升级为 WebSocket 后交给 <see cref="HandleConnectionAsync"/>；
    /// 断连时广播离线在场、清订阅并递减连接计数（finally 保证）。
    /// </summary>
    private static void MapStreamEndpoint(WebApplication app, HubContext hub)
    {
        var metrics = hub.Metrics;
        var broadcaster = hub.Broadcaster;
        var presenceSubscribers = hub.PresenceSubscribers;
        var auditSubscribers = hub.AuditSubscribers;

        app.Map("/v1/stream", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("expected websocket upgrade");
                return;
            }

            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            metrics.ClientOpened();
            var connection = broadcaster.Add(socket);
            try
            {
                await HandleConnectionAsync(connection, hub, ctx.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // 服务停机/客户端取消：正常退出
            }
            catch (Exception ex)
            {
                // 连接循环内的异常（此前会静默终止连接，客户端只看到 socket 被 abort，无从排查）
                Console.Error.WriteLine($"[hub] connection {connection.Id} failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                await BroadcastPresenceAsync(broadcaster, presenceSubscribers, connection, PresenceStatuses.Offline, CancellationToken.None);
                auditSubscribers.TryRemove(connection.Id, out _);
                broadcaster.Remove(connection.Id);
                metrics.ClientClosed();
            }
        });
    }
}
