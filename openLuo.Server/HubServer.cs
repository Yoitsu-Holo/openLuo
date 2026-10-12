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

/// <summary>
/// 最小 Hub：HTTP 控制面（health/version/sessions） + WebSocket 数据面
/// （hello/welcome、session.open、turn.submit → 真流式事件 → turn.final）。
/// 内核经 <see cref="IAgentRuntime"/> 注入，Hub 不感知内核实现。
/// </summary>
public static partial class HubServer
{
    public static async Task RunAsync(
        IAgentRuntime runtime, HubServerOptions options, IRuntimeDirectory? directory = null,
        IConfigService? config = null, IJobService? jobs = null, ISchedulerService? scheduler = null,
        ILogStore? logs = null, HubAuthOptions? auth = null, IAssetStore? assets = null,
        ITokenStore? tokenStore = null, IOutputQueue? outputQueue = null, ITraceStore? traces = null,
        HubConfirmationGate? confirmationGate = null, IStateReader? stateReader = null, CancellationToken ct = default)
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

        // 依赖与共享状态在此装配一次：端点映射与数据面方法此后只接收 hub（见 HubContext）
        var hub = new HubContext
        {
            Runtime = runtime,
            Options = options,
            Metrics = metrics,
            Broadcaster = broadcaster,
            Tokens = tokens,
            AuthOptions = authOptions,
            PresenceSubscribers = presenceSubscribers,
            AuditSubscribers = auditSubscribers,
            PendingConfirmations = pendingConfirmations,
            Features = features,
            StartedAt = startedAt,
            Cancellation = ct,
            Directory = directory,
            Config = config,
            Jobs = jobs,
            Scheduler = scheduler,
            Logs = logs,
            Assets = assets,
            OutputQueue = outputQueue,
            Traces = traces,
            ConfirmationGate = confirmationGate,
            StateReader = stateReader,
            Guard = new HubRequestGuard(options.Security),
        };

        MapAuthEndpoints(app, hub);
        MapSystemEndpoints(app, hub);

        MapSessionEndpoints(app, hub);

        MapCapabilityEndpoints(app, hub);

        MapConfigEndpoints(app, hub);

        MapObservabilityEndpoints(app, hub);

        MapJobEndpoints(app, hub);
        MapScheduleEndpoints(app, hub);

        MapLogEndpoints(app, hub);

        MapAssetEndpoints(app, hub);

        MapStreamEndpoint(app, hub);

        await app.StartAsync(ct);
        Console.WriteLine($"[hub] listening on {options.Listen} (protocol v{ProtocolInfo.MajorVersion})");
        await app.WaitForShutdownAsync(ct);
    }

    private static IResult Json(Envelope envelope) => Results.Json(envelope, ProtocolJson.Options);

    private static IResult Json(Envelope envelope, int statusCode) =>
        Results.Json(envelope, ProtocolJson.Options, statusCode: statusCode);

    /// <summary>201 Created + `Location`（REST：创建类端点）。</summary>
    private static IResult Created(HttpContext ctx, string location, Envelope envelope)
    {
        ctx.Response.Headers.Location = location;
        return Json(envelope, StatusCodes.Status201Created);
    }

    /// <summary>201 Created（没有独立资源地址的创建，如签发令牌）。</summary>
    private static IResult Created(HttpContext ctx, Envelope envelope) =>
        Json(envelope, StatusCodes.Status201Created);

    private static IResult Error(int errorCode, string message) =>
        Results.Json(EnvelopeFactory.CreateError(EventTypes.Error, errorCode, message),
            ProtocolJson.Options, statusCode: ErrorCodes.HttpStatusOf(errorCode));

    /// <summary>从标准 `Content-Disposition` 头取文件名（`attachment; filename="x.png"`；缺省 null）。</summary>
    private static string? FileNameOf(HttpRequest request)
    {
        var raw = request.Headers[HttpRules.FileNameHeader].ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var marker = HttpRules.FileNameParameter + "=";
        var idx = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? null : raw[(idx + marker.Length)..].Trim().Trim('"');
    }

    /// <summary>
    /// 错误写回（带 HTTP 状态码）：映射表在 <see cref="ErrorCodes.HttpStatusOf"/>（Protocol 持有，客户端/文档同源），
    /// 鉴权守卫、入口治理与端点错误共用这一条路径。
    /// </summary>
    private static async Task WriteErrorAsync(HttpContext ctx, int errorCode, string message)
    {
        ctx.Response.StatusCode = ErrorCodes.HttpStatusOf(errorCode);
        ctx.Response.ContentType = HttpRules.JsonContentType;
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

        // 订阅方可能设了会话过滤（presence.subscribe{sessionIds}）：只看"与过滤集合有交集"的连接的在场变化
        var targets = subscribers.Values
            .Where(subscriber => subscriber.SeesPresenceOf(connection.Subscriptions))
            .ToList();
        if (targets.Count == 0)
            return;

        var envelope = EnvelopeFactory.Create(EventTypes.PresenceUpdated, PresenceOf(connection, status));
        await broadcaster.DeliverToConnectionsAsync(targets, envelope, ct);
    }

    /// <summary>资产归属：资产带 sessionId 且请求显式指定了不同的 sessionId → 拒绝（§9；未指定则不校验）。</summary>
    private static IResult? CheckAssetAccess(HttpContext ctx, openLuo.Core.Interfaces.AssetInfo info)
    {
        var requested = ctx.Request.Query[HttpQueryKeys.SessionId].ToString();
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

    private static async Task HandleConnectionAsync(HubConnection connection, HubContext hub, CancellationToken ct)
    {
        // 局部别名：依赖统一来自 hub，方法体保持逐字不变
        var runtime = hub.Runtime;
        var options = hub.Options;
        var features = hub.Features;
        var metrics = hub.Metrics;
        var tokens = hub.Tokens;
        var assets = hub.Assets;
        var broadcaster = hub.Broadcaster;
        var presenceSubscribers = hub.PresenceSubscribers;
        var outputQueue = hub.OutputQueue;
        var traces = hub.Traces;
        var auditSubscribers = hub.AuditSubscribers;
        var pendingConfirmations = hub.PendingConfirmations;
        var guard = hub.Guard;

        var socket = connection.Socket;
        var buffer = new byte[64 * 1024];
        var sessionId = string.Empty;

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var json = await ReceiveTextAsync(socket, buffer, ct);
            if (json is null)
                break;

            // 入口治理（§4.9）：单帧上限——异常大的帧不是本协议客户端，回 2002 后断开。
            if (Encoding.UTF8.GetByteCount(json) > guard.MaxFrameBytes)
            {
                await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error, ErrorCodes.ProtocolBadEnvelope,
                    $"frame too large: exceeds {guard.MaxFrameBytes} bytes"), ct);
                break;
            }

            Envelope? envelope;
            try
            {
                envelope = ProtocolJson.Deserialize<Envelope>(json);
            }
            catch (JsonException)
            {
                await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error, ErrorCodes.ProtocolBadEnvelope, "invalid envelope"), ct);
                continue;
            }

            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
                continue;

            // 入口治理（§4.9）：按连接（有 clientId 用 clientId）限流；hello 必须先放行，否则客户端连不上。
            if (envelope.Type != MessageTypes.Hello
                && !guard.Allow(string.IsNullOrEmpty(connection.ClientId) ? connection.Id : connection.ClientId))
            {
                await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error, ErrorCodes.RateLimited,
                    guard.RateLimitHint, replyTo: envelope.Id), ct);
                continue;
            }

            switch (envelope.Type)
            {
                case MessageTypes.Hello:
                {
                    var hello = envelope.DataAs<HelloCommand>() ?? new HelloCommand();
                    if (hello.ProtocolVersion != ProtocolInfo.MajorVersion)
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
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
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
                            ErrorCodes.ProtocolBadEnvelope, "message.append requires sessionId", replyTo: envelope.Id), ct);
                        break;
                    }

                    await runtime.AppendMessageAsync(append.SessionId, append.SenderName, append.Text,
                        await MapBlocksAsync(assets, append.Blocks, ct), ct);
                    break;
                }

                case MessageTypes.OutputAck:
                {
                    // 多端投递回执：任一端 ack 即视为该输出已投递（不阻塞其它端，也不回复）。
                    var ack = envelope.DataAs<OutputAckCommand>();
                    if (ack is not null && outputQueue is not null)
                        await outputQueue.AckAsync(ack.Sequence, ct);
                    break;
                }

                case MessageTypes.OutputFail:
                {
                    // permanent=true 放弃该输出（以固定失败占位）；false 保留可重试。
                    var fail = envelope.DataAs<OutputFailCommand>();
                    if (fail is not null && outputQueue is not null)
                        await outputQueue.FailAsync(fail.Sequence, fail.Permanent, ct);
                    break;
                }

                case MessageTypes.SessionSubscribe:
                case MessageTypes.SessionUnsubscribe:
                {
                    var sessionRef = envelope.DataAs<SessionRef>();
                    if (sessionRef is null || string.IsNullOrWhiteSpace(sessionRef.SessionId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
                            ErrorCodes.ProtocolBadEnvelope, $"{envelope.Type} requires sessionId", replyTo: envelope.Id), ct);
                        break;
                    }

                    // 入口治理（§4.9）：会话 id 白名单（字母/数字/-/_），挡掉路径穿越与分隔符注入。
                    if (!guard.ValidResourceId(sessionRef.SessionId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error, ErrorCodes.SessionInvalid,
                            "invalid sessionId: only letters, digits, '-' and '_' are allowed", replyTo: envelope.Id), ct);
                        break;
                    }

                    if (envelope.Type == MessageTypes.SessionSubscribe)
                    {
                        // 新加入会话 → 广播在场（让设了 sessionIds 过滤的订阅者看到"谁进了我关心的会话"）
                        if (connection.Subscribe(sessionRef.SessionId))
                            await BroadcastPresenceAsync(broadcaster, presenceSubscribers, connection, PresenceStatuses.Online, ct);
                    }
                    else
                    {
                        connection.Unsubscribe(sessionRef.SessionId);
                    }
                    break;
                }

                case MessageTypes.SessionResume:
                {
                    var resume = envelope.DataAs<SessionResumeCommand>();
                    if (resume is null || string.IsNullOrWhiteSpace(resume.SessionId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
                            ErrorCodes.ProtocolBadEnvelope, "session.resume requires sessionId", replyTo: envelope.Id), ct);
                        break;
                    }

                    // 入口治理（§4.9）：同 session.subscribe 的 id 白名单。
                    if (!guard.ValidResourceId(resume.SessionId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error, ErrorCodes.SessionInvalid,
                            "invalid sessionId: only letters, digits, '-' and '_' are allowed", replyTo: envelope.Id), ct);
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
                    var subscription = envelope.DataAs<PresenceSubscription>();
                    connection.SetPresenceFilter(subscription?.SessionIds);
                    presenceSubscribers[connection.Id] = connection;
                    foreach (var peer in broadcaster.Connections.Where(peer => connection.SeesPresenceOf(peer.Subscriptions)))
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
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
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
                    // 自己也在该会话里（presence 过滤以会话为粒度；回合事件本就会投给发起连接，故不产生重复）。
                    // 新加入会话时广播一次在场，否则设了 sessionIds 过滤的订阅者永远看不到"某人进入我关心的会话"。
                    if (connection.Subscribe(session.SessionId))
                        await BroadcastPresenceAsync(broadcaster, presenceSubscribers, connection, PresenceStatuses.Online, ct);
                    await SendAsync(socket, EnvelopeFactory.Create(
                        EventTypes.SessionOpened, WireMapper.ToDto(session),
                        sessionId: sessionId, replyTo: envelope.Id), ct);
                    break;
                }

                case MessageTypes.TurnSubmit:
                {
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
                            ErrorCodes.SessionNotFound, "session not open", replyTo: envelope.Id), ct);
                        break;
                    }

                    var req = envelope.DataAs<TurnRequestDto>() ?? new TurnRequestDto();

                    // 入口治理（§4.9）：结构校验（空文本放行以支持"仅媒体块"回合；超长/含 NUL 拒绝）+
                    // 严格模式下的启发式（默认关，见 HubSecurityPolicy.StrictInputValidation）。
                    if (!guard.ValidTurnText(req.Text))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error, ErrorCodes.TurnInvalid,
                            $"turn text rejected: at most {guard.Policy.MaxInputLength} characters, no NUL"
                            + (guard.Policy.StrictInputValidation ? ", no SQL keywords" : string.Empty),
                            replyTo: envelope.Id), ct);
                        break;
                    }

                    metrics.TurnStarted();
                    // 回合脱离连接循环投递：否则同一连接在回合进行中读不到后续命令（turn.cancel 永远送不进来）。
                    // 发送侧由 HubConnection 的信号量串行化；同一会话的多个回合由内核 per-session 闸串行执行。
                    var turnSessionId = sessionId;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await RunTurnStreamAsync(connection, hub, turnSessionId, req, envelope.Id, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            // 停机/连接断开：回合内部已处理"客户端取消"那种情形
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[hub] turn failed (session {turnSessionId}): {ex.GetType().Name}: {ex.Message}");
                        }
                    }, CancellationToken.None);
                    break;
                }

                case MessageTypes.TurnCancel:
                {
                    var cancel = envelope.DataAs<TurnCancelCommand>();
                    if (cancel is null || string.IsNullOrWhiteSpace(cancel.TurnId))
                    {
                        await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
                            ErrorCodes.ProtocolBadEnvelope, "turn.cancel requires turnId", replyTo: envelope.Id), ct);
                        break;
                    }

                    if (hub.Turns.TryGetValue(cancel.TurnId, out var turnCts))
                    {
                        // 取消成功不另行回复：该回合会以 turn.final{terminationReason:"cancelled"} 收尾
                        turnCts.Cancel();
                        break;
                    }

                    await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
                        ErrorCodes.TurnCancelled, $"turn not running: {cancel.TurnId}", replyTo: envelope.Id), ct);
                    break;
                }

                default:
                {
                    var known = ProtocolRegistry.IsKnown(envelope.Type);
                    await SendAsync(socket, EnvelopeFactory.CreateError(EventTypes.Error,
                        known ? ErrorCodes.ProtocolNotImplemented : ErrorCodes.ProtocolUnknownType,
                        known
                            ? $"type not handled: {envelope.Type}"
                            : $"unknown type: {envelope.Type}",
                        replyTo: envelope.Id), ct);
                    break;
                }
            }
        }
    }

    private static async Task RunTurnStreamAsync(
        HubConnection connection, HubContext hub, string sessionId,
        TurnRequestDto req, string requestId, CancellationToken ct)
    {
        // 局部别名：依赖统一来自 hub，方法体保持逐字不变
        var broadcaster = hub.Broadcaster;
        var runtime = hub.Runtime;
        var metrics = hub.Metrics;
        var assets = hub.Assets;
        var traces = hub.Traces;

        var turnId = string.IsNullOrWhiteSpace(req.TurnId) ? $"turn_{ProtocolIds.NewUlid()}" : req.TurnId!;

        // 进行中回合登记（turn.cancel 用）：**必须在 turn.accepted 之前**登记，
        // 否则客户端"刚收到 accepted 就取消"会落空；与宿主 ct 联动，回合结束/取消后清理。
        using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        hub.Turns[turnId] = turnCts;

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

        try
        {
            await foreach (var evt in runtime.StreamTurnAsync(turnRequest, turnCts.Token))
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
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 被客户端 turn.cancel 取消（非服务停机）：以 cancelled 收尾，保证客户端总能拿到终局
            success = false;
            termination = TerminationReasons.Cancelled;
            await broadcaster.DeliverAsync(EnvelopeFactory.Create(EventTypes.TurnFinal,
                new TurnResultDto { TurnId = turnId, Success = false, TerminationReason = TerminationReasons.Cancelled, Outputs = [] },
                sessionId: sessionId, replyTo: requestId), sessionId: sessionId, includeConnectionId: connection.Id, ct: ct);
        }
        finally
        {
            hub.Turns.TryRemove(turnId, out _);
        }

        traces?.CompleteTurn(turnId, success, termination, finalText);
        await PushStateUpdateAsync(hub, sessionId, ct, connection.Id);
    }

    /// <summary>
    /// 回合结束后按需广播 <c>state.updated</c>：先按会话拿到 **subjectId**（状态按 subject 分区，与记忆 owner 一致），
    /// 再读该 subject 的状态；**只在该 subject 的状态版本推进时**发一次（否则完全静默）——
    /// <c>patch</c> 是相对上次广播**发生变化的键**，首次广播为全部值。
    /// 新订阅者可用 <c>GET /v1/sessions/{id}/state</c> 取全量快照（协议没有"订阅即快照"语义）。
    /// </summary>
    private static async Task PushStateUpdateAsync(
        HubContext hub, string sessionId, CancellationToken ct, string? includeConnectionId = null)
    {
        var reader = hub.StateReader;
        if (reader is null)
            return;

        var session = await hub.Runtime.GetSessionAsync(sessionId, ct);
        if (session is null)
            return;

        var view = reader.TryGet(session.SubjectId);
        var previous = hub.StateBroadcast.TryGetValue(session.SubjectId, out var last)
            ? last
            : (Version: -1L, Values: (JsonNode?)null);
        if (view is null || view.Version <= previous.Version)
            return;

        var current = JsonSerializer.SerializeToNode(view.Values, ProtocolJson.Options);
        JsonNode? patch = current;
        if (previous.Values is JsonObject previousObject && current is JsonObject currentObject)
        {
            var delta = new JsonObject();
            foreach (var pair in currentObject)
            {
                if (previousObject[pair.Key]?.ToJsonString() != pair.Value?.ToJsonString())
                    delta[pair.Key] = pair.Value?.DeepClone();
            }

            // 版本推进但值无可观测变化（例如同值重写）：只更新缓存，不发事件
            if (delta.Count == 0)
            {
                hub.StateBroadcast[session.SubjectId] = (view.Version, current);
                return;
            }

            patch = delta;
        }

        hub.StateBroadcast[session.SubjectId] = (view.Version, current);
        await hub.Broadcaster.DeliverAsync(EnvelopeFactory.Create(EventTypes.StateUpdated, new StateUpdatedEvent
        {
            SessionId = sessionId,
            Version = view.Version,
            Patch = patch,
        }, sessionId: sessionId), sessionId: sessionId, includeConnectionId: includeConnectionId, ct: ct);
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
    private static async Task RunProactiveTurnAsync(HubContext hub, ScheduleDue due, CancellationToken ct)
    {
        // 局部别名：依赖统一来自 hub，方法体保持逐字不变
        var runtime = hub.Runtime;
        var broadcaster = hub.Broadcaster;
        var metrics = hub.Metrics;
        var assets = hub.Assets;
        var traces = hub.Traces;

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
            await PushStateUpdateAsync(hub, due.SessionId, ct);
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
