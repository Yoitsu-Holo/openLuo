using System.Net;
using System.Net.Sockets;
using System.Reflection;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Client;
using openLuo.Protocol;
using openLuo.Server;
using Xunit;

namespace openLuo.E2E.Tests;

/// <summary>
/// 协议表面一致性：每条**已声明**的命令必须要么被服务端实现（可预期行为），要么在下面的"预留清单"里
/// 且发送时回 <see cref="ErrorCodes.ProtocolNotImplemented"/>。
/// 目的：防止规范与实现再次漂移（docs/architecture/hub-protocol.md 的 `v1 未实现` 标注 + §6.1/§6.2）。
/// </summary>
public sealed class HubProtocolSurfaceTests
{
    /// <summary>服务端 <c>HubServer.HandleConnectionAsync</c> 实际处理的命令；新增实现时**必须**同步这里。</summary>
    private static readonly HashSet<string> Implemented = new(StringComparer.Ordinal)
    {
        MessageTypes.Hello,
        MessageTypes.SessionOpen,
        MessageTypes.SessionSubscribe,
        MessageTypes.SessionUnsubscribe,
        MessageTypes.SessionResume,
        MessageTypes.MessageAppend,
        MessageTypes.PresenceSubscribe,
        MessageTypes.PresenceUnsubscribe,
        MessageTypes.AuditSubscribe,
        MessageTypes.ConfirmResponse,
        MessageTypes.Ping,
        MessageTypes.TurnSubmit,
    };

    /// <summary>已声明但 v1 未实现的命令（文档 §6.1 标 `v1 未实现`）：发送必须回 2004。</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        MessageTypes.SessionClose,
        MessageTypes.TurnCancel,
        MessageTypes.OutputAck,
        MessageTypes.OutputFail,
        MessageTypes.ConfigGet,
        MessageTypes.ConfigSet,
        MessageTypes.ConfigDel,
        MessageTypes.DeviceReport,
        MessageTypes.AvatarCommand,
    };

    [Fact]
    public void 每条声明命令都被分类为已实现或预留()
    {
        var declared = typeof(MessageTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(declared.Except(Implemented).Except(Reserved));   // 新声明必须被分类
        Assert.Empty(Implemented.Except(declared));                    // 清单不得残留已删除的常量
        Assert.Empty(Reserved.Except(declared));
        Assert.Empty(Implemented.Intersect(Reserved));                 // 两类互斥
    }

    [Fact]
    public async Task 预留命令回_not_implemented_未声明类型回_unknown_type()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var run = HubServer.RunAsync(
            new SessionfulStubRuntime(),
            new HubServerOptions { Listen = $"http://127.0.0.1:{port}" },
            ct: cts.Token);

        var client = await ConnectWithRetryAsync($"ws://127.0.0.1:{port}/v1/stream", cts.Token);
        try
        {
            foreach (var type in Reserved.Order(StringComparer.Ordinal))
            {
                var reply = await RequestAsync(client, type, cts.Token);
                Assert.Equal(ErrorCodes.ProtocolNotImplemented, reply.ErrorCode);
                Assert.Contains("type not handled", reply.ErrorMsg, StringComparison.Ordinal);
            }

            var bogus = await RequestAsync(client, "openluo.not.a.real.type", cts.Token);
            Assert.Equal(ErrorCodes.ProtocolUnknownType, bogus.ErrorCode);
            Assert.Contains("unknown type", bogus.ErrorMsg, StringComparison.Ordinal);
        }
        finally
        {
            // 先关连接：否则 Kestrel 停机要等这条 WS 连接 drain，测试会白等数十秒
            await client.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { /* 取消退出 */ }
        }
    }

    /// <summary>发一条命令并等它的回复（`replyTo` 匹配）；其它无关事件直接跳过。</summary>
    private static async Task<Envelope> RequestAsync(HubClient client, string type, CancellationToken ct)
    {
        var sent = EnvelopeFactory.Create<object?>(type, null);
        await client.SendAsync(sent, ct);

        while (true)
        {
            var reply = await client.ReceiveAsync(ct) ?? throw new IOException("hub closed the connection");
            if (reply.ReplyTo == sent.Id)
                return reply;
        }
    }

    private static async Task<HubClient> ConnectWithRetryAsync(string streamUrl, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                // 显式传空 token：避免测试进程的 OPENLUO_HUB_TOKEN 影响结果（默认允许匿名）
                return await HubClient.ConnectAsync(streamUrl, "surface-test", "cli", token: string.Empty, ct: ct);
            }
            catch (HubConnectException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, ct);
            }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task 多客户端以相同conversation开会话_落到同一会话()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var run = HubServer.RunAsync(
            new SessionfulStubRuntime(),
            new HubServerOptions { Listen = $"http://127.0.0.1:{port}" },
            ct: cts.Token);
        var url = $"ws://127.0.0.1:{port}/v1/stream";

        var first = await ConnectWithRetryAsync(url, cts.Token);
        var second = await ConnectWithRetryAsync(url, cts.Token);
        try
        {
            // PC 客户端与手机 QQ 私聊配同一 conversationId → 同一段对话（Hub 透传 conversationId 并回传复用后的 sessionId）
            var a = await OpenSessionAsync(first, "builtin-rin", "companion", "main", cts.Token);
            var b = await OpenSessionAsync(second, "builtin-rin", "companion", "main", cts.Token);
            Assert.Equal(a.SessionId, b.SessionId);
            Assert.Equal("main", b.ConversationId);

            // 不同对话仍各自独立（不是"把所有上下文塞到一起"）
            var other = await OpenSessionAsync(first, "builtin-rin", "companion", "qq-group-9", cts.Token);
            Assert.NotEqual(a.SessionId, other.SessionId);

            // 两端都可订阅同一会话（回合事件会扇出给两端）
            foreach (var client in new[] { first, second })
                await client.SendAsync(EnvelopeFactory.Create(MessageTypes.SessionSubscribe, new SessionRef { SessionId = a.SessionId }), cts.Token);
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { /* 取消退出 */ }
        }
    }

    private static async Task<SessionDto> OpenSessionAsync(
        HubClient client, string subjectId, string agentId, string conversationId, CancellationToken ct)
    {
        var sent = EnvelopeFactory.Create(MessageTypes.SessionOpen, new SessionOpenCommand
        {
            SubjectId = subjectId,
            AgentId = agentId,
            ConversationId = conversationId,
        });
        await client.SendAsync(sent, ct);

        while (true)
        {
            var reply = await client.ReceiveAsync(ct) ?? throw new IOException("hub closed the connection");
            if (reply.ReplyTo == sent.Id)
                return reply.DataAs<SessionDto>() ?? throw new InvalidOperationException($"no session payload in '{reply.Type}'");
        }
    }

    /// <summary>
    /// 最小运行时桩：只实现**会话寻址**（按 subjectId+agentId+conversationId 复用，未给 conversationId 则新建），
    /// 用于验证 Hub 是否把 conversationId 透传并回传复用后的 sessionId；其余成员一旦被调用即说明测试越界。
    /// </summary>
    private sealed class SessionfulStubRuntime : IAgentRuntime
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AgentSession> _bySession = new(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _byConversation = new(StringComparer.OrdinalIgnoreCase);

        public Task<AgentSession> OpenSessionAsync(SessionOpenRequest request, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(request.ConversationId)
                && _byConversation.TryGetValue(Key(request.SubjectId, request.AgentId, request.ConversationId), out var existingId)
                && _bySession.TryGetValue(existingId, out var existing))
                return Task.FromResult(existing);

            var conversationId = request.ConversationId ?? request.SessionId;
            var session = new AgentSession
            {
                SessionId = request.SessionId, SubjectId = request.SubjectId, AgentId = request.AgentId, ConversationId = conversationId,
            };
            _bySession[request.SessionId] = session;
            _byConversation[Key(request.SubjectId, request.AgentId, conversationId)] = request.SessionId;
            return Task.FromResult(session);
        }

        public Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_bySession.TryGetValue(sessionId, out var s) ? s : null);

        public Task<IReadOnlyList<AgentSession>> ListSessionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentSession>>(_bySession.Values.ToList());

        public Task<bool> CloseSessionAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_bySession.TryRemove(sessionId, out _));

        public Task<TurnResult> RunTurnAsync(TurnRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TurnEvent> StreamTurnAsync(TurnRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendMessageAsync(string sessionId, string? senderName, string text, IReadOnlyList<object>? blocks = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetContextSummaryAsync(string sessionId, CancellationToken ct = default) => throw new NotSupportedException();

        private static string Key(string subjectId, string agentId, string conversationId) => $"{subjectId}\u001f{agentId}\u001f{conversationId}";
    }
}
