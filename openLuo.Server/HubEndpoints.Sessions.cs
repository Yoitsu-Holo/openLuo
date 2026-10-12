using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using openLuo.Capabilities.Core;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：会话）。</summary>
public static partial class HubServer
{
    /// <summary>会话域：创建 / 列举 / 读取 / 关闭 + 上下文摘要（§5.4）。</summary>
    private static void MapSessionEndpoints(WebApplication app, HubContext hub)
    {
        var runtime = hub.Runtime;
        var auditSubscribers = hub.AuditSubscribers;
        string? Actor(HttpContext ctx) => hub.Actor(ctx);

        app.MapPost(HttpPaths.Sessions, async (HttpContext ctx, CancellationToken requestCt) =>
        {
            var req = await JsonSerializer.DeserializeAsync<CreateSessionRequest>(
                ctx.Request.Body, ProtocolJson.Options, requestCt) ?? new CreateSessionRequest();

            var session = await runtime.OpenSessionAsync(new SessionOpenRequest
            {
                SessionId = $"sess_{ProtocolIds.NewUlid()}",
                SubjectId = req.SubjectId,
                AgentId = req.AgentId,
                ClientType = req.ClientType,
                ClientId = req.ClientId,
                ConversationId = req.ConversationId,
            }, requestCt);

            await AuditAsync(auditSubscribers, "session.open", Actor(ctx), session.SessionId, "ok", null, requestCt);
            return Created(ctx, HttpPaths.Session(session.SessionId),
                EnvelopeFactory.Create(EventTypes.SessionOpened, WireMapper.ToDto(session, req.ClientType, req.ClientId)));
        });

        app.MapGet(HttpPaths.Sessions, async (CancellationToken requestCt) =>
        {
            var sessions = await runtime.ListSessionsAsync(requestCt);
            return Json(EnvelopeFactory.Create(HttpResponseTypes.Sessions, new SessionsResponse
            {
                Sessions = sessions.Select(s => WireMapper.ToDto(s)).ToList(),
            }));
        });

        app.MapGet(HttpPaths.SessionTemplate, async (string id, CancellationToken requestCt) =>
        {
            var session = await runtime.GetSessionAsync(id, requestCt);
            return session is null
                ? Error(ErrorCodes.SessionNotFound, $"session not found: {id}")
                : Json(EnvelopeFactory.Create(EventTypes.SessionOpened, WireMapper.ToDto(session)));
        });

        app.MapDelete(HttpPaths.SessionTemplate, async (string id, HttpContext ctx, CancellationToken requestCt) =>
        {
            var removed = await runtime.CloseSessionAsync(id, requestCt);
            await AuditAsync(auditSubscribers, "session.close", Actor(ctx), id, removed ? "ok" : "not_found", null, requestCt);
            return removed
                ? Json(EnvelopeFactory.Create(EventTypes.SessionClosed, new SessionClosedEvent { SessionId = id }))
                : Error(ErrorCodes.SessionNotFound, $"session not found: {id}");
        });

        app.MapGet(HttpPaths.SessionContextTemplate, async (string id, CancellationToken requestCt) =>
        {
            var summary = await runtime.GetContextSummaryAsync(id, requestCt);
            return summary is null
                ? Error(ErrorCodes.SessionNotFound, $"session not found: {id}")
                : Json(EnvelopeFactory.Create(HttpResponseTypes.Context, new ContextResponse { Summary = summary }));
        });

        // 世界状态只读投影（§5.4）。未接入状态读取端口时不注册（与 capabilities/logs 等条件端点一致）。
        if (hub.StateReader is { } stateReader)
        {
            app.MapGet(HttpPaths.SessionStateTemplate, async (string id, CancellationToken requestCt) =>
            {
                var session = await runtime.GetSessionAsync(id, requestCt);
                if (session is null)
                    return Error(ErrorCodes.SessionNotFound, $"session not found: {id}");

                // 状态按 subject 分区（会话只是寻址单位，与记忆 owner 一致）
                var view = stateReader.TryGet(session.SubjectId);
                return view is null
                    ? Error(ErrorCodes.SessionNotFound, $"no state for session: {id}")
                    : Json(EnvelopeFactory.Create(HttpResponseTypes.State, new StateResponse
                    {
                        Version = view.Version,
                        Values = JsonSerializer.SerializeToNode(view.Values, ProtocolJson.Options),
                    }));
            });
        }
    }
}
