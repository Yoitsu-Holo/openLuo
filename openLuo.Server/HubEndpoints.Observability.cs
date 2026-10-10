using Microsoft.AspNetCore.Builder;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：观测 / 审计，§5.11）。</summary>
public static partial class HubServer
{
    /// <summary>观测域（admin-only）：运行指标 + 回合轨迹回放。轨迹依赖 <see cref="ITraceStore"/>，未接入则只注册指标。</summary>
    private static void MapObservabilityEndpoints(WebApplication app, HubContext hub)
    {
        var runtime = hub.Runtime;
        var startedAt = hub.StartedAt;
        var metrics = hub.Metrics;
        var traces = hub.Traces;

        app.MapGet("/v1/metrics", async (CancellationToken requestCt) =>
        {
            var sessions = await runtime.ListSessionsAsync(requestCt);
            return Json(EnvelopeFactory.Create("metrics", new MetricsDto
            {
                UptimeSec = (DateTimeOffset.UtcNow - startedAt).TotalSeconds,
                SessionsActive = sessions.Count,
                TurnsTotal = metrics.Turns,
                ErrorsTotal = metrics.Errors,
                ClientsConnected = metrics.Clients,
            }));
        });

        if (traces is null)
            return;

        app.MapGet("/v1/traces/{turnId}", (string turnId) =>
        {
            var trace = traces.Get(turnId);
            return trace is null
                ? Error(ErrorCodes.TraceNotFound, $"trace not found: {turnId}")
                : Json(EnvelopeFactory.Create("trace", new TurnTraceDto
                {
                    TurnId = trace.TurnId,
                    SessionId = trace.SessionId,
                    Events = trace.Events.Select(e => new TraceEventDto
                    {
                        Ts = e.Ts,
                        Type = e.Type,
                        Data = TryParseNode(e.Data),
                    }).ToList(),
                }));
        });
    }
}
