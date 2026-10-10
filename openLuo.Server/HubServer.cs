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

/// <summary>在线连接广播（作业 / 调度 / 通知等非回合事件）。</summary>
internal sealed class HubBroadcaster
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, WebSocket> _sockets = new(StringComparer.Ordinal);

    public void Add(string id, WebSocket socket) => _sockets[id] = socket;

    public void Remove(string id) => _sockets.TryRemove(id, out _);

    public async Task BroadcastAsync(Envelope envelope)
    {
        if (_sockets.IsEmpty)
            return;

        var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(envelope));
        foreach (var (_, socket) in _sockets)
        {
            if (socket.State != WebSocketState.Open)
                continue;
            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // 断开：忽略（连接循环会清理）
            }
        }
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
        ILogStore? logs = null, CancellationToken ct = default)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(options.Listen);

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        var startedAt = DateTimeOffset.UtcNow;
        var metrics = new HubMetrics();
        var broadcaster = new HubBroadcaster();
        string[] features = [Features.Streaming, Features.MultiSession, Features.Confirm, Features.Config];

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

        app.MapDelete("/v1/sessions/{id}", async (string id, CancellationToken requestCt) =>
        {
            var removed = await runtime.CloseSessionAsync(id, requestCt);
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

            app.MapDelete("/v1/jobs/{id}", (string id) =>
                jobs.Cancel(id)
                    ? Json(EnvelopeFactory.Create("job", new JobDto { Id = id, Status = JobStatuses.Cancelled, CompletedAt = DateTimeOffset.UtcNow }))
                    : Error(ErrorCodes.JobNotFound, $"job not running: {id}"));

            // 作业事件 → 广播给所有在线客户端（§6.4）
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in jobs.WatchAsync(ct))
                        await broadcaster.BroadcastAsync(ToEnvelope(evt));
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
                    return Json(EnvelopeFactory.Create("schedule", WireMapper.ToDto(info)));
                }
                catch (InvalidOperationException ex)
                {
                    return Error(ErrorCodes.ScheduleInvalid, ex.Message);
                }
            });

            app.MapDelete("/v1/schedules/{id}", (string id) =>
                scheduler.Remove(id)
                    ? Json(EnvelopeFactory.Create("schedule", new { deleted = true }))
                    : Error(ErrorCodes.ScheduleNotFound, $"schedule not found: {id}"));

            // 到期 → 发起主动回合（§5.8/§6.4）
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var due in scheduler.WatchAsync(ct))
                        await RunProactiveTurnAsync(runtime, broadcaster, due, metrics, ct);
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
            var clientKey = ProtocolIds.NewUlid();
            broadcaster.Add(clientKey, socket);
            try
            {
                await HandleConnectionAsync(socket, runtime, options, features, metrics, ctx.RequestAborted);
            }
            finally
            {
                broadcaster.Remove(clientKey);
                metrics.ClientClosed();
            }
        });

        await app.StartAsync(ct);
        Console.WriteLine($"[hub] listening on {options.Listen} (protocol v{ProtocolInfo.MajorVersion})");
        await app.WaitForShutdownAsync(ct);
    }

    private static IResult Json(Envelope envelope) => Results.Json(envelope, ProtocolJson.Options);

    private static IResult Error(int errorCode, string message) =>
        Results.Json(EnvelopeFactory.CreateError(EventTypes.Error, errorCode, message), ProtocolJson.Options);

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

    /// <summary>wire 图像块（kind=image）→ 内核 Block（供视觉模型消费）。</summary>
    private static IReadOnlyList<object> MapBlocks(IReadOnlyList<JsonNode>? blocks)
    {
        if (blocks is null || blocks.Count == 0)
            return [];

        var mapped = new List<object>();
        foreach (var block in blocks)
        {
            if (block is not JsonObject obj)
                continue;

            var kind = obj["kind"]?.GetValue<string>();
            if (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase))
            {
                mapped.Add(new openLuo.Core.Models.ImageBlock
                {
                    Kind = openLuo.Core.Models.BlockKind.Image,
                    DataUri = obj["dataUri"]?.GetValue<string>() ?? string.Empty,
                    MimeType = obj["mime"]?.GetValue<string>() ?? "image/jpeg",
                    AssetId = obj["assetId"]?.GetValue<string>() ?? obj["name"]?.GetValue<string>() ?? string.Empty,
                    Name = obj["name"]?.GetValue<string>() ?? string.Empty,
                });
            }
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
        WebSocket socket, IAgentRuntime runtime, HubServerOptions options, string[] features, HubMetrics metrics, CancellationToken ct)
    {
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

                    await runtime.AppendMessageAsync(append.SessionId, append.SenderName, append.Text, MapBlocks(append.Blocks), ct);
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
                    await RunTurnStreamAsync(socket, runtime, sessionId, req, envelope.Id, metrics, ct);
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
        WebSocket socket, IAgentRuntime runtime, string sessionId, TurnRequestDto req, string requestId, HubMetrics metrics, CancellationToken ct)
    {
        var turnId = string.IsNullOrWhiteSpace(req.TurnId) ? $"turn_{ProtocolIds.NewUlid()}" : req.TurnId!;

        await SendAsync(socket, EnvelopeFactory.Create(EventTypes.TurnAccepted,
            new TurnAcceptedEvent { TurnId = turnId, SessionId = sessionId },
            sessionId: sessionId, replyTo: requestId), ct);

        var turnRequest = new TurnRequest
        {
            SessionId = sessionId,
            TurnId = turnId,
            SourceId = req.SourceId ?? "wire",
            ChannelId = req.ChannelId ?? sessionId,
            ActorId = req.ActorId ?? "player",
            SenderName = req.SenderName,
            Text = req.Text,
            Blocks = MapBlocks(req.Blocks),
        };

        await foreach (var evt in runtime.StreamTurnAsync(turnRequest, ct))
        {
            var envelope = MapTurnEvent(evt, turnId, sessionId);

            if (evt.Kind == "final" && evt.Payload is TurnResult { Success: false })
                metrics.ErrorReported();   // ErrorsTotal = 失败回合数

            if (envelope is not null)
                await SendAsync(socket, envelope, ct);
        }
    }

    /// <summary>内核回合事件 → wire 事件（回合与主动回合共用）。</summary>
    private static Envelope? MapTurnEvent(TurnEvent evt, string turnId, string sessionId) => evt.Kind switch
    {
        "decision" when evt.Payload is int step =>
            EnvelopeFactory.Create(EventTypes.Decision, WireMapper.ToDecision(turnId, step), sessionId: sessionId),
        "tool_call" when evt.Payload is CapabilityCall call =>
            EnvelopeFactory.Create(EventTypes.ToolCall, WireMapper.ToToolCall(turnId, call), sessionId: sessionId),
        "tool_result" when evt.Payload is CapabilityResult result =>
            EnvelopeFactory.Create(EventTypes.ToolResult, WireMapper.ToToolResult(turnId, result), sessionId: sessionId),
        "output" when evt.Payload is OutputItem item =>
            EnvelopeFactory.Create(EventTypes.Output, WireMapper.ToDto(item), sessionId: sessionId),
        "final" when evt.Payload is TurnResult result =>
            EnvelopeFactory.Create(EventTypes.TurnFinal, WireMapper.ToDto(result, turnId), sessionId: sessionId),
        _ => null,
    };

    /// <summary>服务端发起的回合（调度到期等）：广播 turn.started 并推送回合事件。</summary>
    private static async Task RunProactiveTurnAsync(
        IAgentRuntime runtime, HubBroadcaster broadcaster, ScheduleDue due, HubMetrics metrics, CancellationToken ct)
    {
        var turnId = $"turn_{ProtocolIds.NewUlid()}";

        await broadcaster.BroadcastAsync(EnvelopeFactory.Create(EventTypes.TurnStarted, new TurnStartedEvent
        {
            TurnId = turnId,
            SessionId = due.SessionId,
            Origin = TurnOrigins.Scheduled,
            Trigger = due.Id,
        }, sessionId: due.SessionId));

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
                var envelope = MapTurnEvent(evt, turnId, due.SessionId);
                if (envelope is not null)
                    await broadcaster.BroadcastAsync(envelope);
            }
        }
        catch (Exception ex)
        {
            await broadcaster.BroadcastAsync(EnvelopeFactory.CreateError(
                EventTypes.Error, ErrorCodes.ServerInternal, ex.Message, sessionId: due.SessionId));
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
