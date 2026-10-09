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

/// <summary>
/// 最小 Hub：HTTP 控制面（health/version/sessions）+ WebSocket 数据面
/// （hello/welcome、session.open、turn.submit → 真流式事件 → turn.final）。
/// 内核经 <see cref="IAgentRuntime"/> 注入，Hub 不感知内核实现。
/// </summary>
public static class HubServer
{
    public static async Task RunAsync(
        IAgentRuntime runtime, HubServerOptions options, IRuntimeDirectory? directory = null,
        IConfigService? config = null, CancellationToken ct = default)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(options.Listen);

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        var startedAt = DateTimeOffset.UtcNow;
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

        app.Map("/v1/stream", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("expected websocket upgrade");
                return;
            }

            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            await HandleConnectionAsync(socket, runtime, options, features, ctx.RequestAborted);
        });

        await app.StartAsync(ct);
        Console.WriteLine($"[hub] listening on {options.Listen} (protocol v{ProtocolInfo.MajorVersion})");
        await app.WaitForShutdownAsync(ct);
    }

    private static IResult Json(Envelope envelope) => Results.Json(envelope, ProtocolJson.Options);

    private static IResult Error(int errorCode, string message) =>
        Results.Json(EnvelopeFactory.CreateError(EventTypes.Error, errorCode, message), ProtocolJson.Options);

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
        WebSocket socket, IAgentRuntime runtime, HubServerOptions options, string[] features, CancellationToken ct)
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
                    await RunTurnStreamAsync(socket, runtime, sessionId, req, envelope.Id, ct);
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
        WebSocket socket, IAgentRuntime runtime, string sessionId, TurnRequestDto req, string requestId, CancellationToken ct)
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
            Blocks = req.Blocks?.Cast<object>().ToList(),
        };

        await foreach (var evt in runtime.StreamTurnAsync(turnRequest, ct))
        {
            var envelope = evt.Kind switch
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

            if (envelope is not null)
                await SendAsync(socket, envelope, ct);
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
