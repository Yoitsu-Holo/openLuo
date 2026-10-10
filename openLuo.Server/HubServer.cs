using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using openLuo.Capabilities.Core;
using openLuo.Core.Interfaces;
using openLuo.Capabilities.Core.Models;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>Hub 运行参数（后续由 <c>server.jsonc</c> 驱动）。</summary>
public sealed class HubServerOptions
{
    public string Listen { get; init; } = $"http://127.0.0.1:{ProtocolInfo.DefaultPort}";
    public bool AllowAnonymous { get; init; } = true;
    public string ServerVersion { get; init; } = "0.1.0";

    /// <summary>单资产字节上限（超出回 `7002 asset.too_large`）。</summary>
    public long AssetMaxBytes { get; init; } = 16L * 1024 * 1024;

    /// <summary>高危能力确认等待上限（秒）；超时视为拒绝。</summary>
    public int ConfirmTimeoutSeconds { get; init; } = 60;
}

/// <summary>Hub 运行指标（进程内计数）。</summary>
internal sealed class HubMetrics
{
    private long _turns;
    private long _errors;
    private int _clients;

    public long Turns => Interlocked.Read(ref _turns);
    public long Errors => Interlocked.Read(ref _errors);
    public int Clients => Volatile.Read(ref _clients);

    public void TurnStarted() => Interlocked.Increment(ref _turns);
    public void ErrorReported() => Interlocked.Increment(ref _errors);
    public void ClientOpened() => Interlocked.Increment(ref _clients);
    public void ClientClosed() => Interlocked.Decrement(ref _clients);
}

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

/// <summary>
/// 在线连接注册与投递（§6.4）：非回合事件按**显式订阅**投递；
/// 回合事件投给「发起连接 + 该会话订阅者」；`targetClientId` 定向优先。
/// </summary>
internal sealed class HubBroadcaster
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HubConnection> _connections = new(StringComparer.Ordinal);

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

/// <summary>
/// 最小 Hub：HTTP 控制面（health/version/sessions） + WebSocket 数据面
/// （hello/welcome、session.open、turn.submit → 真流式事件 → turn.final）。
/// 内核经 <see cref="IAgentRuntime"/> 注入，Hub 不感知内核实现。
/// </summary>
public static class HubServer
{
    public static async Task RunAsync(
        IAgentRuntime runtime, HubServerOptions options, IRuntimeDirectory? directory = null,
        IConfigService? config = null, IJobService? jobs = null, ISchedulerService? scheduler = null,
        ILogStore? logs = null, HubAuthOptions? auth = null, IAssetStore? assets = null,
        ITokenStore? tokenStore = null, IOutputQueue? outputQueue = null, ITraceStore? traces = null,
        HubConfirmationGate? confirmationGate = null, CancellationToken ct = default)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(options.Listen);

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        var startedAt = DateTimeOffset.UtcNow;
        var metrics = new HubMetrics();
        var broadcaster = new HubBroadcaster();
        var presenceSubscribers = new ConcurrentDictionary<string, HubConnection>(StringComparer.Ordinal);
        var auditSubscribers = new ConcurrentDictionary<string, HubConnection>(StringComparer.Ordinal);
        var authOptions = auth ?? new HubAuthOptions();
        var tokens = new TokenRegistry(authOptions, tokenStore);

        // 审计：HTTP 请求方身份（由 Bearer token 解析）
        string? Actor(HttpContext ctx) =>
            tokens.ResolveInfo(TokenRegistry.BearerOf(ctx.Request.Headers.Authorization.ToString())).ClientId;

        // 高危能力确认（§8）：向会话订阅者推 confirm.request，等待 confirm.response；超时/无人 → 拒绝
        var pendingConfirmations = new ConcurrentDictionary<string, TaskCompletionSource<bool>>(StringComparer.Ordinal);
        confirmationGate?.Attach(async (request, requestCt) =>
        {
            var requestId = ProtocolIds.NewUlid();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingConfirmations[requestId] = completion;

            await broadcaster.DeliverAsync(EnvelopeFactory.Create(EventTypes.ConfirmRequest, new ConfirmRequestEvent
            {
                RequestId = requestId,
                TurnId = request.TurnId,
                CanonicalId = request.CanonicalId,
                Risk = request.Risk,
                Summary = request.Summary,
                ArgsPreview = TryParseNode(request.ArgsPreview),
            }, sessionId: request.SessionId), sessionId: request.SessionId, ct: requestCt);

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.ConfirmTimeoutSeconds)));
                using var registration = timeout.Token.Register(() => completion.TrySetResult(false));
                return await completion.Task;
            }
            finally
            {
                pendingConfirmations.TryRemove(requestId, out _);
            }
        });
        string[] features = [Features.Streaming, Features.MultiSession, Features.Confirm, Features.Config];

        // 权限守卫（§4.8）：admin-only 路径未鉴权 → 3001，越权 → 3002。
        string[] adminPrefixes = ["/v1/config", "/v1/logs", "/v1/metrics", "/v1/schedules", "/v1/traces"];
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? string.Empty;
            var needsAdmin = adminPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                || (path.StartsWith("/v1/jobs", StringComparison.OrdinalIgnoreCase) && !HttpMethods.IsGet(ctx.Request.Method));

            var needsUser = path.StartsWith("/v1/assets", StringComparison.OrdinalIgnoreCase);
            if (needsAdmin || needsUser)
            {
                var role = tokens.Authorize(TokenRegistry.BearerOf(ctx.Request.Headers.Authorization.ToString()));
                if (role is null)
                {
                    await WriteErrorAsync(ctx, ErrorCodes.AuthUnauthorized, "missing or invalid token");
                    return;
                }
                var requiredRank = needsAdmin ? HubRoles.Rank(HubRoles.Admin) : HubRoles.Rank(HubRoles.User);
                if (HubRoles.Rank(role) < requiredRank)
                {
                    await WriteErrorAsync(ctx, ErrorCodes.AuthForbidden, $"requires role: {(needsAdmin ? HubRoles.Admin : HubRoles.User)}");
                    return;
                }
            }
            await next();
        });

        app.MapPost("/v1/auth/token", async (HttpContext ctx, CancellationToken requestCt) =>
        {
            var request = await JsonSerializer.DeserializeAsync<TokenRequest>(ctx.Request.Body, ProtocolJson.Options, requestCt)
                          ?? new TokenRequest();
            var issued = tokens.TryIssue(request, out var response, out var errorCode, out var error);
            if (issued)
                await AuditAsync(auditSubscribers, "auth.token", request.ClientId, response.Role, "ok", null, requestCt);

            return issued
                ? Json(EnvelopeFactory.Create("auth.token", response))
                : Results.Json(
                    EnvelopeFactory.CreateError(EventTypes.Error, errorCode, error),
                    ProtocolJson.Options,
                    statusCode: StatusCodes.Status401Unauthorized);
        });

        app.MapGet("/v1/health", () => Json(EnvelopeFactory.Create("health", new HealthDto
        {
            Status = "ok",
            UptimeSec = (DateTimeOffset.UtcNow - startedAt).TotalSeconds,
        })));

        app.MapGet("/v1/version", () => Json(EnvelopeFactory.Create("version", new VersionDto
        {
            ServerVersion = options.ServerVersion,
            ProtocolVersion = ProtocolInfo.MajorVersion,
            Features = features,
        })));

        app.MapPost("/v1/sessions", async (HttpContext ctx, CancellationToken requestCt) =>
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
            return Json(EnvelopeFactory.Create("session.opened", WireMapper.ToDto(session, req.ClientType, req.ClientId)));
        });

        app.MapGet("/v1/sessions", async (CancellationToken requestCt) =>
        {
            var sessions = await runtime.ListSessionsAsync(requestCt);
            return Json(EnvelopeFactory.Create("sessions", new SessionsResponse
            {
                Sessions = sessions.Select(s => WireMapper.ToDto(s)).ToList(),
            }));
        });

        app.MapGet("/v1/sessions/{id}", async (string id, CancellationToken requestCt) =>
        {
            var session = await runtime.GetSessionAsync(id, requestCt);
            return session is null
                ? Error(ErrorCodes.SessionNotFound, $"session not found: {id}")
                : Json(EnvelopeFactory.Create(EventTypes.SessionOpened, WireMapper.ToDto(session)));
        });

        app.MapDelete("/v1/sessions/{id}", async (string id, HttpContext ctx, CancellationToken requestCt) =>
        {
            var removed = await runtime.CloseSessionAsync(id, requestCt);
            await AuditAsync(auditSubscribers, "session.close", Actor(ctx), id, removed ? "ok" : "not_found", null, requestCt);
            return removed
                ? Json(EnvelopeFactory.Create(EventTypes.SessionClosed, new SessionClosedEvent { SessionId = id }))
                : Error(ErrorCodes.SessionNotFound, $"session not found: {id}");
        });

        if (directory is not null)
        {
            app.MapGet("/v1/capabilities", async (string? sessionId, CancellationToken requestCt) =>
            {
                var capabilities = await directory.ListCapabilitiesAsync(sessionId, requestCt);
                return Json(EnvelopeFactory.Create("capabilities", new CapabilitiesResponse
                {
                    Version = 1,
                    Capabilities = capabilities.Select(WireMapper.ToDto).ToList(),
                }));
            });
        }

        if (config is not null)
        {
            app.MapGet("/v1/config", () => Json(EnvelopeFactory.Create("config", new ConfigListResponse
            {
                Namespaces = config.ListNamespaces().Select(n => new ConfigNamespaceDto
                {
                    Namespace = n.Namespace,
                    Source = n.Source,
                    Overridden = n.Overridden,
                    UpdatedAt = n.UpdatedAt,
                }).ToList(),
            })));

            app.MapGet("/v1/config/{ns}", async (string ns, CancellationToken requestCt) =>
            {
                var view = await config.GetAsync(ns, requestCt);
                return view is null
                    ? Error(ErrorCodes.ConfigNamespaceNotFound, $"config namespace not found: {ns}")
                    : Json(EnvelopeFactory.Create("config", new ConfigGetResponse
                    {
                        Namespace = view.Namespace,
                        Source = view.Source,
                        Values = Mask(view.Values),
                        Overrides = Mask(view.Overrides),
                    }));
            });

            app.MapPost("/v1/config/{ns}", async (string ns, HttpContext ctx, CancellationToken requestCt) =>
            {
                var body = await JsonSerializer.DeserializeAsync<ConfigSetRequest>(ctx.Request.Body, ProtocolJson.Options, requestCt)
                           ?? new ConfigSetRequest();
                if (body.Values is null)
                    return Error(ErrorCodes.ConfigInvalidValue, "values is required");

                var view = await config.SetAsync(ns, body.Values, body.Persist, requestCt);
                await AuditAsync(auditSubscribers, "config.set", Actor(ctx), ns, "ok", null, requestCt);
                return Json(EnvelopeFactory.Create("config", new ConfigSetResponse
                {
                    Namespace = view.Namespace,
                    Source = view.Source,
                    Values = Mask(view.Values),
                }));
            });

            app.MapDelete("/v1/config/{ns}", async (string ns, HttpContext ctx, CancellationToken requestCt) =>
            {
                var persist = string.Equals(ctx.Request.Query["persist"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
                var view = await config.DeleteAsync(ns, persist, requestCt);
                await AuditAsync(auditSubscribers, "config.delete", Actor(ctx), ns, view is null ? "not_found" : "ok", null, requestCt);
                return view is null
                    ? Error(ErrorCodes.ConfigNamespaceNotFound, $"config namespace not found: {ns}")
                    : Json(EnvelopeFactory.Create("config", new ConfigDeleteResponse
                    {
                        Namespace = view.Namespace,
                        Source = view.Source,
                        Values = Mask(view.Values),
                    }));
            });
        }

        app.MapGet("/v1/sessions/{id}/context", async (string id, CancellationToken requestCt) =>
        {
            var summary = await runtime.GetContextSummaryAsync(id, requestCt);
            return summary is null
                ? Error(ErrorCodes.SessionNotFound, $"session not found: {id}")
                : Json(EnvelopeFactory.Create("context", new ContextResponse { Summary = summary }));
        });

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

        if (jobs is not null)
        {
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

            static Envelope ToEnvelope(JobEvent evt) => evt.Kind switch
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

        if (scheduler is not null)
        {
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
                        await RunProactiveTurnAsync(runtime, broadcaster, due, metrics, assets, traces, ct);
                }
                catch (OperationCanceledException)
                {
                    // 服务关闭
                }
            }, CancellationToken.None);
        }

        // 日志热查询（§5.11）。注：鉴权待 token 落地后收紧为 admin-only。
        if (logs is not null)
        {
            app.MapGet("/v1/logs", async (string? from, string? to, string? level, string? module, string? category,
                string? keyword, string? sessionId, string? turnId, int? limit, long? beforeId,
                CancellationToken requestCt) =>
            {
                var query = new LogQuery
                {
                    From = ParseTime(from),
                    To = ParseTime(to),
                    MinLevel = level,
                    Module = module,
                    Category = category,
                    Keyword = keyword,
                    SessionId = sessionId,
                    TurnId = turnId,
                    Limit = limit ?? 200,
                    BeforeId = beforeId,
                };

                var items = await logs.QueryAsync(query, requestCt);
                return Json(EnvelopeFactory.Create("logs", new LogsResponse
                {
                    Items = items.Select(WireMapper.ToDto).ToList(),
                    NextBeforeId = items.Count >= Math.Clamp(query.Limit, 1, 1000) ? items[^1].Id : null,
                }));
            });

            app.MapGet("/v1/logs/archive", async (string? date, string? category, string? keyword, string? level,
                int? limit, CancellationToken requestCt) =>
            {
                var items = await logs.SearchArchiveAsync(new ArchiveLogQuery(
                    Date: string.IsNullOrWhiteSpace(date) ? "*" : date,
                    Category: category,
                    Keyword: keyword,
                    MinLevel: level,
                    Limit: limit ?? 200), requestCt);

                return Json(EnvelopeFactory.Create("logs", new LogsResponse
                {
                    Items = items.Select(WireMapper.ToDto).ToList(),
                }));
            });

            app.MapGet("/v1/logs/stats", async (string? from, string? to, string? bucket, CancellationToken requestCt) =>
            {
                var toTs = ParseTime(to) ?? DateTimeOffset.UtcNow;
                var fromTs = ParseTime(from) ?? toTs.AddHours(-1);
                var buckets = await logs.StatsAsync(fromTs, toTs, ParseBucket(bucket), requestCt);
                return Json(EnvelopeFactory.Create("logs", new LogStatsResponse
                {
                    Buckets = buckets.Select(b => new LogBucketDto { Start = b.Start, Counts = b.Counts }).ToList(),
                }));
            });
        }

        // 资产（§5.10/§9）：二进制经引用传递，不再内联 data URL。
        if (assets is not null)
        {
            app.MapPost("/v1/assets", async (HttpContext ctx, string? sessionId, string? fileName, CancellationToken requestCt) =>
            {
                var mime = string.IsNullOrWhiteSpace(ctx.Request.ContentType)
                    ? "application/octet-stream"
                    : ctx.Request.ContentType;

                using var buffer = new MemoryStream();
                await ctx.Request.Body.CopyToAsync(buffer, requestCt);
                var bytes = buffer.ToArray();

                if (bytes.Length == 0)
                    return Error(ErrorCodes.AssetInvalid, "asset is empty");
                if (bytes.Length > options.AssetMaxBytes)
                    return Error(ErrorCodes.AssetTooLarge, $"asset exceeds limit: {bytes.Length} > {options.AssetMaxBytes}");

                var headerName = ctx.Request.Headers["X-File-Name"].ToString();
                try
                {
                    var info = await assets.PutAsync(bytes, mime, sessionId,
                        fileName ?? (string.IsNullOrWhiteSpace(headerName) ? null : headerName), requestCt);

                    await AuditAsync(auditSubscribers, "asset.upload", Actor(ctx), info.Id, "ok",
                        new JsonObject { ["mime"] = info.Mime, ["size"] = info.Size }, requestCt);

                    return Json(EnvelopeFactory.Create("asset", new UploadAssetResponse
                    {
                        Asset = ToAssetRef(info),
                        Meta = ToAssetMeta(info),
                    }));
                }
                catch (InvalidOperationException ex)
                {
                    return Error(ErrorCodes.AssetTooLarge, ex.Message);
                }
            });

            app.MapGet("/v1/assets/{id}", async (string id, HttpContext ctx, CancellationToken requestCt) =>
            {
                var blob = await assets.GetAsync(id, requestCt);
                if (blob is null)
                    return Error(ErrorCodes.AssetNotFound, $"asset not found: {id}");

                if (CheckAssetAccess(ctx, blob.Info) is { } denied)
                    return denied;

                return Results.File(blob.Bytes, blob.Info.Mime);
            });

            app.MapMethods("/v1/assets/{id}", ["HEAD"], (string id, HttpContext ctx) =>
            {
                var info = assets.Stat(id);
                if (info is null)
                    return Error(ErrorCodes.AssetNotFound, $"asset not found: {id}");
                return CheckAssetAccess(ctx, info) ?? Json(EnvelopeFactory.Create("asset", ToAssetMeta(info)));
            });

            app.MapDelete("/v1/assets/{id}", async (string id, HttpContext ctx) =>
            {
                var info = assets.Stat(id);
                if (info is not null && CheckAssetAccess(ctx, info) is { } denied)
                    return denied;
                var deleted = assets.Delete(id);
                await AuditAsync(auditSubscribers, "asset.delete", Actor(ctx), id, deleted ? "ok" : "not_found", null, default);
                return deleted
                    ? Json(EnvelopeFactory.Create("asset", new { deleted = true, id }))
                    : Error(ErrorCodes.AssetNotFound, $"asset not found: {id}");
            });
        }

        if (traces is not null)
        {
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
                await HandleConnectionAsync(connection, runtime, options, features, metrics, tokens, assets, broadcaster,
                    presenceSubscribers, outputQueue, traces, auditSubscribers, pendingConfirmations, ctx.RequestAborted);
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

        await app.StartAsync(ct);
        Console.WriteLine($"[hub] listening on {options.Listen} (protocol v{ProtocolInfo.MajorVersion})");
        await app.WaitForShutdownAsync(ct);
    }

    private static IResult Json(Envelope envelope) => Results.Json(envelope, ProtocolJson.Options);

    private static IResult Error(int errorCode, string message) =>
        Results.Json(EnvelopeFactory.CreateError(EventTypes.Error, errorCode, message),
            ProtocolJson.Options, statusCode: HttpStatusFor(errorCode));

    /// <summary>错误码 → HTTP 状态码（按 §4.5 分段归一）。</summary>
    private static int HttpStatusFor(int code) => code switch
    {
        ErrorCodes.AuthUnauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.AuthForbidden or ErrorCodes.ConfigReadOnly => StatusCodes.Status403Forbidden,

        ErrorCodes.SessionNotFound or ErrorCodes.AssetNotFound or ErrorCodes.JobNotFound
            or ErrorCodes.ScheduleNotFound or ErrorCodes.ConfigNamespaceNotFound
            or ErrorCodes.DeviceNotFound or ErrorCodes.TraceNotFound => StatusCodes.Status404NotFound,

        ErrorCodes.AssetTooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorCodes.RateLimited or ErrorCodes.SessionLimitExceeded => StatusCodes.Status429TooManyRequests,
        ErrorCodes.TurnBusy or ErrorCodes.TurnCancelled or ErrorCodes.CapabilityConfirmationRequired => StatusCodes.Status409Conflict,
        ErrorCodes.PresenceUnavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.JobFailed or ErrorCodes.ConfigPersistFailed or ErrorCodes.ServerInternal => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>中间件使用的错误写回（带 HTTP 状态码）。</summary>
    private static async Task WriteErrorAsync(HttpContext ctx, int errorCode, string message)
    {
        ctx.Response.StatusCode = errorCode == ErrorCodes.AuthUnauthorized
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(ProtocolJson.Serialize(
            EnvelopeFactory.CreateError(EventTypes.Error, errorCode, message)));
    }

    /// <summary>审计事件广播（`audit.event`）给已订阅的 admin 连接。</summary>
    private static async Task AuditAsync(
        System.Collections.Concurrent.ConcurrentDictionary<string, HubConnection> subscribers,
        string action, string? actorClientId, string? target, string result, JsonNode? details = null,
        CancellationToken ct = default)
    {
        if (subscribers.IsEmpty)
            return;

        var envelope = EnvelopeFactory.Create(EventTypes.AuditEvent, new AuditEventDto
        {
            ClientId = actorClientId,
            Action = action,
            Target = target,
            Result = result,
            Details = details,
        });

        foreach (var connection in subscribers.Values)
            await connection.SendAsync(envelope, ct);
    }

    private static JsonNode? TryParseNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try { return JsonNode.Parse(json); }
        catch { return JsonValue.Create(json); }
    }

    private static PresenceUpdatedEvent PresenceOf(HubConnection connection, string status) => new()
    {
        ClientId = connection.ClientId ?? connection.Id,
        Status = status,
        Presence = new PresenceDto
        {
            ClientId = connection.ClientId ?? connection.Id,
            ClientType = connection.ClientType ?? "unknown",
            Status = status,
            Sessions = connection.Subscriptions,
            ConnectedAt = DateTimeOffset.UtcNow,
        },
    };

    private static async Task BroadcastPresenceAsync(
        HubBroadcaster broadcaster, ConcurrentDictionary<string, HubConnection> subscribers,
        HubConnection connection, string status, CancellationToken ct)
    {
        if (subscribers.IsEmpty)
            return;

        var envelope = EnvelopeFactory.Create(EventTypes.PresenceUpdated, PresenceOf(connection, status));
        await broadcaster.DeliverToConnectionsAsync(subscribers.Values.ToList(), envelope, ct);
    }

    /// <summary>资产归属：资产带 sessionId 且请求显式指定了不同的 sessionId → 拒绝（§9；未指定则不校验）。</summary>
    private static IResult? CheckAssetAccess(HttpContext ctx, openLuo.Core.Interfaces.AssetInfo info)
    {
        var requested = ctx.Request.Query["sessionId"].ToString();
        if (info.SessionId is { Length: > 0 } owner && !string.IsNullOrWhiteSpace(requested)
            && !string.Equals(requested, owner, StringComparison.Ordinal))
        {
            return Error(ErrorCodes.AuthForbidden, "asset belongs to another session");
        }
        return null;
    }

    private static AssetRefDto ToAssetRef(AssetInfo info) => new()
    {
        Id = info.Id,
        Mime = info.Mime,
        Size = info.Size,
        Checksum = info.Checksum,
    };

    private static AssetMetaDto ToAssetMeta(AssetInfo info) => new()
    {
        Id = info.Id,
        Mime = info.Mime,
        Size = info.Size,
        Checksum = info.Checksum,
        CreatedAt = info.CreatedAt,
    };

    /// <summary>时间参数：Unix 毫秒或 ISO8601。</summary>
    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (long.TryParse(value, out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>时间桶：<c>1m</c>/<c>5m</c>/<c>1h</c>/<c>1d</c>，默认 1 分钟。</summary>
    private static TimeSpan ParseBucket(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TimeSpan.FromMinutes(1);
        var text = value.Trim().ToLowerInvariant();
        var number = int.TryParse(text[..^1], out var n) ? Math.Max(1, n) : 1;
        return text[^1] switch
        {
            's' => TimeSpan.FromSeconds(number),
            'm' => TimeSpan.FromMinutes(number),
            'h' => TimeSpan.FromHours(number),
            'd' => TimeSpan.FromDays(number),
            _ => TimeSpan.FromMinutes(1),
        };
    }

    /// <summary>wire 图像块 → 内核 Block：支持内联 <c>dataUri</c> 或 <c>assetRef</c>（后者按 id 拉取字节）。</summary>
    private static async Task<IReadOnlyList<object>> MapBlocksAsync(
        IAssetStore? assets, IReadOnlyList<JsonNode>? blocks, CancellationToken ct)
    {
        if (blocks is null || blocks.Count == 0)
            return [];

        var mapped = new List<object>();
        foreach (var block in blocks)
        {
            if (block is not JsonObject obj)
                continue;

            var kind = obj["kind"]?.GetValue<string>();
            if (!string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase))
                continue;

            var mime = obj["mime"]?.GetValue<string>() ?? "image/jpeg";
            var dataUri = obj["dataUri"]?.GetValue<string>();
            var assetId = obj["assetRef"]?.GetValue<string>() ?? obj["assetId"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(dataUri) && assets is not null && !string.IsNullOrWhiteSpace(assetId))
            {
                var blob = await assets.GetAsync(assetId!, ct);
                if (blob is not null)
                {
                    mime = blob.Info.Mime;
                    dataUri = $"data:{mime};base64,{Convert.ToBase64String(blob.Bytes)}";
                }
            }

            if (string.IsNullOrWhiteSpace(dataUri))
                continue;

            mapped.Add(new openLuo.Core.Models.ImageBlock
            {
                Kind = openLuo.Core.Models.BlockKind.Image,
                DataUri = dataUri,
                MimeType = mime,
                AssetId = assetId ?? obj["name"]?.GetValue<string>() ?? string.Empty,
                Name = obj["name"]?.GetValue<string>() ?? string.Empty,
            });
        }
        return mapped;
    }

    private static readonly string[] SensitiveMarkers = ["key", "secret", "token", "password"];

    /// <summary>敏感字段掩码（值 → `***`），递归处理对象与数组（§5.7）。</summary>
    private static JsonNode? Mask(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => MaskObject(obj),
        JsonArray arr => new JsonArray(arr.Select(Mask).ToArray()),
        _ => node.DeepClone(),
    };

    private static JsonObject MaskObject(JsonObject source)
    {
        var result = new JsonObject();
        foreach (var (key, value) in source)
        {
            var sensitive = SensitiveMarkers.Any(m => key.Contains(m, StringComparison.OrdinalIgnoreCase));
            result[key] = sensitive && value is not null ? JsonValue.Create("***") : Mask(value);
        }
        return result;
    }

    private static async Task HandleConnectionAsync(
        HubConnection connection, IAgentRuntime runtime, HubServerOptions options, string[] features,
        HubMetrics metrics, TokenRegistry tokens, IAssetStore? assets, HubBroadcaster broadcaster,
        ConcurrentDictionary<string, HubConnection> presenceSubscribers, IOutputQueue? outputQueue,
        ITraceStore? traces, ConcurrentDictionary<string, HubConnection> auditSubscribers,
        ConcurrentDictionary<string, TaskCompletionSource<bool>> pendingConfirmations, CancellationToken ct)
    {
        var socket = connection.Socket;
        var buffer = new byte[64 * 1024];
        var sessionId = string.Empty;

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var json = await ReceiveTextAsync(socket, buffer, ct);
            if (json is null)
                break;

            Envelope? envelope;
            try
            {
                envelope = ProtocolJson.Deserialize<Envelope>(json);
            }
            catch (JsonException)
            {
                await SendAsync(socket, EnvelopeFactory.CreateError("error", ErrorCodes.ProtocolBadEnvelope, "invalid envelope"), ct);
                continue;
            }

            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
                continue;

            switch (envelope.Type)
            {
                case MessageTypes.Hello:
                {
                    var hello = envelope.DataAs<HelloCommand>() ?? new HelloCommand();
                    if (hello.ProtocolVersion != ProtocolInfo.MajorVersion)
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError("error",
                            ErrorCodes.ProtocolVersionMismatch,
                            $"protocol major mismatch: client {hello.ProtocolVersion} vs server {ProtocolInfo.MajorVersion}",
                            replyTo: envelope.Id), ct);
                        await socket.CloseAsync(WebSocketCloseStatus.ProtocolError, "version mismatch", ct);
                        return;
                    }

                    var role = tokens.Authorize(hello.Token);
                    if (role is null)
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
                            ErrorCodes.AuthUnauthorized, "missing or invalid token", replyTo: envelope.Id), ct);
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "unauthorized", ct);
                        return;
                    }

                    connection.ClientId = hello.ClientId;
                    connection.ClientType = hello.ClientType;
                    connection.Role = role;
                    await BroadcastPresenceAsync(broadcaster, presenceSubscribers, connection, PresenceStatuses.Online, ct);

                    await SendAsync(socket, EnvelopeFactory.Create(EventTypes.Welcome, new WelcomeEvent
                    {
                        ProtocolVersion = ProtocolInfo.MajorVersion,
                        ServerVersion = options.ServerVersion,
                        ClientId = hello.ClientId,
                        Features = features,
                        HeartbeatIntervalSec = ProtocolInfo.DefaultHeartbeatSec,
                    }, replyTo: envelope.Id), ct);
                    break;
                }

                case MessageTypes.MessageAppend:
                {
                    var append = envelope.DataAs<MessageAppendCommand>();
                    if (append is null || string.IsNullOrWhiteSpace(append.SessionId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError("error",
                            ErrorCodes.ProtocolBadEnvelope, "message.append requires sessionId", replyTo: envelope.Id), ct);
                        break;
                    }

                    await runtime.AppendMessageAsync(append.SessionId, append.SenderName, append.Text,
                        await MapBlocksAsync(assets, append.Blocks, ct), ct);
                    break;
                }

                case MessageTypes.SessionSubscribe:
                case MessageTypes.SessionUnsubscribe:
                {
                    var sessionRef = envelope.DataAs<SessionRef>();
                    if (sessionRef is null || string.IsNullOrWhiteSpace(sessionRef.SessionId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError("error",
                            ErrorCodes.ProtocolBadEnvelope, $"{envelope.Type} requires sessionId", replyTo: envelope.Id), ct);
                        break;
                    }

                    if (envelope.Type == MessageTypes.SessionSubscribe)
                        connection.Subscribe(sessionRef.SessionId);
                    else
                        connection.Unsubscribe(sessionRef.SessionId);
                    break;
                }

                case MessageTypes.SessionResume:
                {
                    var resume = envelope.DataAs<SessionResumeCommand>();
                    if (resume is null || string.IsNullOrWhiteSpace(resume.SessionId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError("error",
                            ErrorCodes.ProtocolBadEnvelope, "session.resume requires sessionId", replyTo: envelope.Id), ct);
                        break;
                    }

                    connection.Subscribe(resume.SessionId);   // 续传隐含订阅
                    if (outputQueue is not null)
                    {
                        foreach (var item in outputQueue.ReadSince(resume.SessionId, resume.SinceSequence))
                        {
                            await connection.SendAsync(EnvelopeFactory.Create(
                                EventTypes.Output, WireMapper.ToDto(item),
                                sessionId: resume.SessionId, replyTo: envelope.Id), ct);
                        }
                    }
                    break;
                }

                case MessageTypes.PresenceSubscribe:
                {
                    presenceSubscribers[connection.Id] = connection;
                    foreach (var peer in broadcaster.Connections)
                        await connection.SendAsync(EnvelopeFactory.Create(EventTypes.PresenceUpdated,
                            PresenceOf(peer, PresenceStatuses.Online)), ct);
                    break;
                }

                case MessageTypes.PresenceUnsubscribe:
                    presenceSubscribers.TryRemove(connection.Id, out _);
                    break;

                case MessageTypes.AuditSubscribe:
                {
                    if (HubRoles.Rank(connection.Role) < HubRoles.Rank(HubRoles.Admin))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
                            ErrorCodes.AuthForbidden, "requires role: admin", replyTo: envelope.Id), ct);
                        break;
                    }
                    auditSubscribers[connection.Id] = connection;
                    break;
                }

                case MessageTypes.ConfirmResponse:
                {
                    var response = envelope.DataAs<ConfirmResponseCommand>();
                    if (response is null || string.IsNullOrWhiteSpace(response.RequestId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError("error",
                            ErrorCodes.ProtocolBadEnvelope, "confirm.response requires requestId", replyTo: envelope.Id), ct);
                        break;
                    }

                    if (pendingConfirmations.TryRemove(response.RequestId, out var pending))
                        pending.TrySetResult(response.Approved);
                    break;
                }

                case MessageTypes.Ping:
                    await SendAsync(socket, EnvelopeFactory.Create(EventTypes.Pong, new PongEvent(), replyTo: envelope.Id), ct);
                    break;

                case MessageTypes.SessionOpen:
                {
                    var open = envelope.DataAs<SessionOpenCommand>() ?? new SessionOpenCommand();
                    var session = await runtime.OpenSessionAsync(new SessionOpenRequest
                    {
                        SessionId = $"sess_{ProtocolIds.NewUlid()}",
                        SubjectId = open.SubjectId,
                        AgentId = open.AgentId,
                        ConversationId = open.ConversationId,
                    }, ct);

                    sessionId = session.SessionId;
                    await SendAsync(socket, EnvelopeFactory.Create(
                        EventTypes.SessionOpened, WireMapper.ToDto(session),
                        sessionId: sessionId, replyTo: envelope.Id), ct);
                    break;
                }

                case MessageTypes.TurnSubmit:
                {
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError("error",
                            ErrorCodes.SessionNotFound, "session not open", replyTo: envelope.Id), ct);
                        break;
                    }

                    var req = envelope.DataAs<TurnRequestDto>() ?? new TurnRequestDto();
                    metrics.TurnStarted();
                    await RunTurnStreamAsync(connection, broadcaster, runtime, sessionId, req, envelope.Id, metrics, assets, traces, ct);
                    break;
                }

                default:
                    await SendAsync(socket, EnvelopeFactory.CreateError("error",
                        ErrorCodes.ProtocolUnknownType,
                        ProtocolRegistry.IsKnown(envelope.Type)
                            ? $"type not handled: {envelope.Type}"
                            : $"unknown type: {envelope.Type}",
                        replyTo: envelope.Id), ct);
                    break;
            }
        }
    }

    private static async Task RunTurnStreamAsync(
        HubConnection connection, HubBroadcaster broadcaster, IAgentRuntime runtime, string sessionId,
        TurnRequestDto req, string requestId, HubMetrics metrics, IAssetStore? assets, ITraceStore? traces, CancellationToken ct)
    {
        var turnId = string.IsNullOrWhiteSpace(req.TurnId) ? $"turn_{ProtocolIds.NewUlid()}" : req.TurnId!;

        traces?.StartTurn(turnId, sessionId, TurnOrigins.Client, DateTimeOffset.UtcNow);
        var success = false;
        string? termination = null;
        string? finalText = null;

        await broadcaster.DeliverAsync(EnvelopeFactory.Create(EventTypes.TurnAccepted,
            new TurnAcceptedEvent { TurnId = turnId, SessionId = sessionId },
            sessionId: sessionId, replyTo: requestId), sessionId: sessionId, includeConnectionId: connection.Id, ct: ct);

        var turnRequest = new TurnRequest
        {
            SessionId = sessionId,
            TurnId = turnId,
            SourceId = req.SourceId ?? "wire",
            ChannelId = req.ChannelId ?? sessionId,
            ActorId = req.ActorId ?? "player",
            SenderName = req.SenderName,
            Text = req.Text,
            Blocks = await MapBlocksAsync(assets, req.Blocks, ct),
        };

        await foreach (var evt in runtime.StreamTurnAsync(turnRequest, ct))
        {
            var envelope = await MapTurnEventAsync(assets, evt, turnId, sessionId, ct);

            if (evt.Kind == "final" && evt.Payload is TurnResult { Success: false })
                metrics.ErrorReported();   // ErrorsTotal = 失败回合数

            if (evt.Kind == "final" && evt.Payload is TurnResult final)
            {
                success = final.Success;
                termination = final.TerminationReason.ToString();
                finalText = final.FinalText;
            }

            if (envelope is not null)
            {
                traces?.AddEvent(turnId, envelope.Type, envelope.Data?.ToJsonString());
                await broadcaster.DeliverAsync(envelope, sessionId: sessionId, includeConnectionId: connection.Id, ct: ct);
            }
        }

        traces?.CompleteTurn(turnId, success, termination, finalText);
    }

    /// <summary>内核回合事件 → wire 事件（回合与主动回合共用）；二进制输出改资产引用（§9）。</summary>
    private static async Task<Envelope?> MapTurnEventAsync(
        IAssetStore? assets, TurnEvent evt, string turnId, string sessionId, CancellationToken ct)
    {
        switch (evt.Kind)
        {
            case "decision" when evt.Payload is int step:
                return EnvelopeFactory.Create(EventTypes.Decision, WireMapper.ToDecision(turnId, step), sessionId: sessionId);
            case "tool_call" when evt.Payload is CapabilityCall call:
                return EnvelopeFactory.Create(EventTypes.ToolCall, WireMapper.ToToolCall(turnId, call), sessionId: sessionId);
            case "tool_result" when evt.Payload is CapabilityResult result:
                return EnvelopeFactory.Create(EventTypes.ToolResult, WireMapper.ToToolResult(turnId, result), sessionId: sessionId);
            case "output" when evt.Payload is OutputItem item:
                return EnvelopeFactory.Create(EventTypes.Output,
                    await ToOutputDtoAsync(assets, item, sessionId, ct), sessionId: sessionId);
            case "final" when evt.Payload is TurnResult result:
            {
                var outputs = new List<OutputDto>(result.Outputs.Count);
                foreach (var output in result.Outputs)
                    outputs.Add(await ToOutputDtoAsync(assets, output, sessionId, ct));

                return EnvelopeFactory.Create(EventTypes.TurnFinal,
                    WireMapper.ToDto(result, turnId) with { Outputs = outputs }, sessionId: sessionId);
            }
            default:
                return null;
        }
    }

    /// <summary>输出项 → wire DTO：二进制（data URL）转存为资产引用；文本/卡片保持内联。</summary>
    private static async Task<OutputDto> ToOutputDtoAsync(
        IAssetStore? assets, OutputItem item, string sessionId, CancellationToken ct)
    {
        var dto = WireMapper.ToDto(item);
        if (assets is null || item.Kind is ReplyItemKind.Text or ReplyItemKind.Card || item.Payload is not string dataUrl)
            return dto;

        var (mime, bytes) = DecodeDataUrl(dataUrl);
        if (bytes is null)
            return dto;

        try
        {
            var info = await assets.PutAsync(bytes, mime ?? "application/octet-stream", sessionId, null, ct);
            return dto with { Payload = null, AssetRef = ToAssetRef(info) };
        }
        catch (InvalidOperationException)
        {
            return dto;   // 超限等：保留内联（降级），不阻塞回合
        }
    }

    /// <summary>解析 <c>data:&lt;mime&gt;;base64,&lt;payload&gt;</c>。</summary>
    private static (string? Mime, byte[]? Bytes) DecodeDataUrl(string value)
    {
        const string marker = "base64,";
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return (null, null);

        var header = value[..index];
        var mime = header.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? header["data:".Length..].Trim().TrimEnd(';')
            : null;

        try
        {
            return (string.IsNullOrWhiteSpace(mime) ? null : mime,
                Convert.FromBase64String(value[(index + marker.Length)..]));
        }
        catch (FormatException)
        {
            return (null, null);
        }
    }

    /// <summary>服务端发起的回合（调度到期等）：广播 turn.started 并推送回合事件。</summary>
    private static async Task RunProactiveTurnAsync(
        IAgentRuntime runtime, HubBroadcaster broadcaster, ScheduleDue due, HubMetrics metrics,
        IAssetStore? assets, ITraceStore? traces, CancellationToken ct)
    {
        var turnId = $"turn_{ProtocolIds.NewUlid()}";

        traces?.StartTurn(turnId, due.SessionId, TurnOrigins.Scheduled, DateTimeOffset.UtcNow);

        await broadcaster.DeliverAsync(EnvelopeFactory.Create(EventTypes.TurnStarted, new TurnStartedEvent
        {
            TurnId = turnId,
            SessionId = due.SessionId,
            Origin = TurnOrigins.Scheduled,
            Trigger = due.Id,
        }, sessionId: due.SessionId), sessionId: due.SessionId, ct: ct);

        metrics.TurnStarted();

        var request = new TurnRequest
        {
            SessionId = due.SessionId,
            TurnId = turnId,
            SourceId = "hub",
            ChannelId = due.SessionId,
            ActorId = "system",
            Text = string.Empty,
        };

        try
        {
            await foreach (var evt in runtime.StreamTurnAsync(request, ct))
            {
                var envelope = await MapTurnEventAsync(assets, evt, turnId, due.SessionId, ct);
                if (envelope is not null)
                {
                    traces?.AddEvent(turnId, envelope.Type, envelope.Data?.ToJsonString());
                    await broadcaster.DeliverAsync(envelope, sessionId: due.SessionId, ct: ct);
                }
            }

            traces?.CompleteTurn(turnId, true, null, null);
        }
        catch (Exception ex)
        {
            await broadcaster.DeliverAsync(EnvelopeFactory.CreateError(
                EventTypes.Error, ErrorCodes.ServerInternal, ex.Message, sessionId: due.SessionId),
                sessionId: due.SessionId, ct: ct);
        }
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType == WebSocketMessageType.Binary)
                continue; // MVP：忽略二进制帧

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    private static async Task SendAsync(WebSocket socket, Envelope envelope, CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(envelope));
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        catch (WebSocketException)
        {
            // 客户端已断开：忽略（连接循环会随之退出）
        }
    }
}
