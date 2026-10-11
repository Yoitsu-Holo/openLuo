using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using openLuo.Core.Interfaces;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：长任务 / 作业，§5.9）。</summary>
public static partial class HubServer
{
    /// <summary>作业域：提交 / 查询 / 取消 + 作业事件广播。依赖 <see cref="IJobService"/>，未接入则整组不注册。</summary>
    private static void MapJobEndpoints(WebApplication app, HubContext hub)
    {
        var jobs = hub.Jobs;
        if (jobs is null)
            return;

        var auditSubscribers = hub.AuditSubscribers;
        var broadcaster = hub.Broadcaster;
        var ct = hub.Cancellation;
        string? Actor(HttpContext ctx) => hub.Actor(ctx);

        app.MapPost("/v1/jobs", async (HttpContext ctx, CancellationToken requestCt) =>
        {
            var body = await JsonSerializer.DeserializeAsync<CreateJobRequest>(ctx.Request.Body, ProtocolJson.Options, requestCt)
                       ?? new CreateJobRequest();
            if (string.IsNullOrWhiteSpace(body.Kind))
                return Error(ErrorCodes.JobInvalid, "kind is required");

            try
            {
                var job = await jobs.SubmitAsync(body.Kind, body.Payload, body.SessionId, requestCt);
                await AuditAsync(auditSubscribers, "job.submit", Actor(ctx), job.Id, "ok", null, requestCt);
                return Json(EnvelopeFactory.Create("job", WireMapper.ToDto(job)));
            }
            catch (InvalidOperationException ex)
            {
                return Error(ErrorCodes.JobInvalid, ex.Message);
            }
        });

        app.MapGet("/v1/jobs/{id}", (string id) =>
        {
            var job = jobs.Get(id);
            return job is null
                ? Error(ErrorCodes.JobNotFound, $"job not found: {id}")
                : Json(EnvelopeFactory.Create("job", WireMapper.ToDto(job)));
        });

        app.MapDelete("/v1/jobs/{id}", async (string id, HttpContext ctx) =>
        {
            var cancelled = jobs.Cancel(id);
            await AuditAsync(auditSubscribers, "job.cancel", Actor(ctx), id, cancelled ? "ok" : "not_found", null, default);
            return cancelled
                ? Json(EnvelopeFactory.Create("job", new JobDto { Id = id, Status = JobStatuses.Cancelled, CompletedAt = DateTimeOffset.UtcNow }))
                : Error(ErrorCodes.JobNotFound, $"job not running: {id}");
        });

        // 作业事件 → 广播给所有在线客户端（§6.4）
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in jobs.WatchAsync(ct))
                    await broadcaster.DeliverAsync(ToEnvelope(evt), sessionId: evt.Job.SessionId, ct: ct);
            }
            catch (OperationCanceledException)
            {
                // 服务关闭
            }
        }, CancellationToken.None);

        static Envelope ToEnvelope(openLuo.Capabilities.Core.JobEvent evt) => evt.Kind switch
        {
            "job.accepted" => EnvelopeFactory.Create(EventTypes.JobAccepted, new JobAcceptedEvent
            {
                JobId = evt.Job.Id, Kind = evt.Job.Kind, SessionId = evt.Job.SessionId,
            }),
            "job.progress" => EnvelopeFactory.Create(EventTypes.JobProgress, new JobProgressEvent
            {
                JobId = evt.Job.Id, Progress = evt.Job.Progress, Message = evt.Job.Message,
            }),
            "job.completed" => EnvelopeFactory.Create(EventTypes.JobCompleted, new JobCompletedEvent
            {
                JobId = evt.Job.Id,
            }),
            _ => EnvelopeFactory.Create(EventTypes.JobFailed, new JobFailedEvent
            {
                JobId = evt.Job.Id, ErrorCode = ErrorCodes.JobFailed, ErrorMsg = evt.ErrorMsg ?? "failed",
            }),
        };
    }
}
