using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using openLuo.Capabilities.Core;
using openLuo.Core.Interfaces;

namespace openLuo.Server;

/// <summary>
/// Hub 一次运行期内的全部依赖与共享状态：由 <see cref="HubServer.RunAsync"/> 装配一次，
/// 之后所有端点映射与数据面方法只接收这一个对象（替代此前 7–14 个参数的长签名）。
/// </summary>
/// <remarks>
/// 本类型刻意只做"依赖容器"，不含行为；<see cref="IAgentRuntime"/> 之外的字段均可为 null
/// （对应宿主未接入该子系统时，相关端点整体不注册）。
/// </remarks>
internal sealed class HubContext
{
    public required IAgentRuntime Runtime { get; init; }
    public required HubServerOptions Options { get; init; }
    public required HubMetrics Metrics { get; init; }
    public required HubBroadcaster Broadcaster { get; init; }
    public required TokenRegistry Tokens { get; init; }
    public required HubAuthOptions AuthOptions { get; init; }
    public required ConcurrentDictionary<string, HubConnection> PresenceSubscribers { get; init; }
    public required ConcurrentDictionary<string, HubConnection> AuditSubscribers { get; init; }
    public required ConcurrentDictionary<string, TaskCompletionSource<bool>> PendingConfirmations { get; init; }
    public required string[] Features { get; init; }
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>服务运行期取消令牌（宿主停机 / 测试取消时触发）。</summary>
    public required CancellationToken Cancellation { get; init; }

    public IRuntimeDirectory? Directory { get; init; }
    public IConfigService? Config { get; init; }
    public IJobService? Jobs { get; init; }
    public ISchedulerService? Scheduler { get; init; }
    public ILogStore? Logs { get; init; }
    public IAssetStore? Assets { get; init; }
    public IOutputQueue? OutputQueue { get; init; }
    public ITraceStore? Traces { get; init; }
    public HubConfirmationGate? ConfirmationGate { get; init; }

    /// <summary>HTTP 请求方身份（由 Bearer token 解析；未鉴权为 null）。</summary>
    public string? Actor(HttpContext ctx) =>
        Tokens.ResolveInfo(TokenRegistry.BearerOf(ctx.Request.Headers.Authorization.ToString())).ClientId;
}
