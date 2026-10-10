using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using openLuo.Core.Interfaces;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：调度，§5.8）。</summary>
public static partial class HubServer
{
    /// <summary>调度域（admin-only）：列举 / 新增 / 删除 + 到期发起主动回合。依赖 <see cref="ISchedulerService"/>。</summary>
    private static void MapScheduleEndpoints(WebApplication app, HubContext hub)
    {
        var scheduler = hub.Scheduler;
        if (scheduler is null)
            return;

        var auditSubscribers = hub.AuditSubscribers;
        var ct = hub.Cancellation;
        string? Actor(HttpContext ctx) => hub.Actor(ctx);

        app.MapGet("/v1/schedules", () => Json(EnvelopeFactory.Create("schedules", new ScheduleListResponse
        {
            Schedules = scheduler.List().Select(WireMapper.ToDto).ToList(),
        })));

        app.MapPost("/v1/schedules", async (HttpContext ctx, CancellationToken requestCt) =>
        {
            var body = await JsonSerializer.DeserializeAsync<CreateScheduleRequest>(ctx.Request.Body, ProtocolJson.Options, requestCt)
                       ?? new CreateScheduleRequest();
            if (string.IsNullOrWhiteSpace(body.SessionId) || string.IsNullOrWhiteSpace(body.Kind))
                return Error(ErrorCodes.ScheduleInvalid, "sessionId and kind are required");

            try
            {
                var info = scheduler.Add(body.SessionId, body.Kind, body.At, body.Cron, body.Payload);
                await AuditAsync(auditSubscribers, "schedule.add", Actor(ctx), info.Id, "ok", null, requestCt);
                return Json(EnvelopeFactory.Create("schedule", WireMapper.ToDto(info)));
            }
            catch (InvalidOperationException ex)
            {
                return Error(ErrorCodes.ScheduleInvalid, ex.Message);
            }
        });

        app.MapDelete("/v1/schedules/{id}", async (string id, HttpContext ctx) =>
        {
            var removed = scheduler.Remove(id);
            await AuditAsync(auditSubscribers, "schedule.remove", Actor(ctx), id, removed ? "ok" : "not_found", null, default);
            return removed
                ? Json(EnvelopeFactory.Create("schedule", new { deleted = true }))
                : Error(ErrorCodes.ScheduleNotFound, $"schedule not found: {id}");
        });

        // 到期 → 发起主动回合（§5.8/§6.4）
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var due in scheduler.WatchAsync(ct))
                    await RunProactiveTurnAsync(hub, due, ct);
            }
            catch (OperationCanceledException)
            {
                // 服务关闭
            }
        }, CancellationToken.None);
    }
}
