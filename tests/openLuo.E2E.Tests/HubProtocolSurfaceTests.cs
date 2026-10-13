using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json.Nodes;
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
        MessageTypes.OutputAck,
        MessageTypes.OutputFail,
        MessageTypes.PresenceSubscribe,
        MessageTypes.PresenceUnsubscribe,
        MessageTypes.AuditSubscribe,
        MessageTypes.ConfirmResponse,
        MessageTypes.Ping,
        MessageTypes.TurnSubmit,
        MessageTypes.TurnCancel,
    };

    /// <summary>已声明但 v1 未实现的命令（文档 §6.1 标 `v1 未实现`）：发送必须回 2004。</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        MessageTypes.SessionClose,
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

    private static async Task<HubClient> ConnectWithRetryAsync(string streamUrl, CancellationToken ct, string clientId = "surface-test")
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                // 显式传空 token：避免测试进程的 OPENLUO_HUB_TOKEN 影响结果（默认允许匿名）
                return await HubClient.ConnectAsync(streamUrl, clientId, "cli", token: string.Empty, ct: ct);
            }
            catch (HubConnectException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, ct);
            }
        }
    }

    [Fact]
    public async Task 会话状态_可HTTP读取_且回合后推送增量变更()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var runtime = new SessionfulStubRuntime();
        var state = new StubStateReader();
        var run = HubServer.RunAsync(
            runtime,
            new HubServerOptions { Listen = $"http://127.0.0.1:{port}" },
            stateReader: state,
            ct: cts.Token);
        var url = $"ws://127.0.0.1:{port}/v1/stream";

        var client = await ConnectWithRetryAsync(url, cts.Token);
        try
        {
            var session = await OpenSessionAsync(client, "builtin-rin", "companion", "main", cts.Token);

            // ① HTTP 只读投影（按会话 → subject 取状态）
            state.Set("builtin-rin", 1, ("mood", "calm"));
            using var http = new HttpClient();
            var body = await http.GetStringAsync($"http://127.0.0.1:{port}/v1/sessions/{session.SessionId}/state", cts.Token);
            Assert.Contains("\"version\":1", body);
            Assert.Contains("calm", body);

            // ② 状态推进 + 回合结束后推送 state.updated，patch 只含变化的键
            state.Set("builtin-rin", 2, ("mood", "happy"), ("energy", 80));
            await client.SendAsync(EnvelopeFactory.Create(MessageTypes.TurnSubmit, new TurnRequestDto { Text = "hi" }), cts.Token);

            var sawState = false;
            var sawFinal = false;
            while (!sawState || !sawFinal)
            {
                var reply = await client.ReceiveAsync(cts.Token) ?? throw new IOException("hub closed the connection");
                if (reply.Type == EventTypes.TurnFinal)
                    sawFinal = true;
                if (reply.Type == EventTypes.StateUpdated)
                {
                    var payload = reply.DataAs<StateUpdatedEvent>() ?? throw new InvalidOperationException("no state.updated payload");
                    Assert.Equal(2, payload.Version);
                    Assert.Equal("happy", payload.Patch?["mood"]?.GetValue<string>());
                    Assert.Equal(80, payload.Patch?["energy"]?.GetValue<int>());
                    sawState = true;
                }
            }
        }
        finally
        {
            await client.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { /* 取消退出 */ }
        }
    }

    /// <summary>状态只读端口的桩：按 subject 存一份版本化视图。</summary>
    private sealed class StubStateReader : IStateReader
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, StateView> _views = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string subjectId, long version, params (string Key, object? Value)[] values) =>
            _views[subjectId] = new StateView(
                version,
                values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase));

        public StateView? TryGet(string subjectId) => _views.TryGetValue(subjectId, out var view) ? view : null;
    }

    [Fact]
    public async Task 输出回执_转发到内核输出队列()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var queue = new RecordingOutputQueue();
        var run = HubServer.RunAsync(
            new SessionfulStubRuntime(),
            new HubServerOptions { Listen = $"http://127.0.0.1:{port}" },
            outputQueue: queue,
            ct: cts.Token);

        var client = await ConnectWithRetryAsync($"ws://127.0.0.1:{port}/v1/stream", cts.Token);
        try
        {
            await client.SendAsync(EnvelopeFactory.Create(MessageTypes.SessionOpen, new SessionOpenCommand
            {
                SubjectId = "builtin-rin", AgentId = "companion", ConversationId = "main",
            }), cts.Token);
            _ = await client.ReceiveAsync(cts.Token);   // session.opened

            await client.SendAsync(EnvelopeFactory.Create(MessageTypes.OutputAck, new OutputAckCommand { Sequence = 7 }), cts.Token);
            await client.SendAsync(EnvelopeFactory.Create(MessageTypes.OutputFail,
                new OutputFailCommand { Sequence = 8, Permanent = true }), cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((queue.Acked.Count == 0 || queue.Failed.Count == 0) && DateTime.UtcNow < deadline)
                await Task.Delay(50, cts.Token);

            Assert.Equal([7L], queue.Acked);
            Assert.Equal([(8L, true)], queue.Failed);
        }
        finally
        {
            await client.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { /* 取消退出 */ }
        }
    }

    /// <summary>记录回执的队列桩：协议侧只需证明 ack/fail 被转发到内核输出队列。</summary>
    private sealed class RecordingOutputQueue : IOutputQueue
    {
        public List<long> Acked { get; } = [];
        public List<(long Sequence, bool Permanent)> Failed { get; } = [];

        public ValueTask<long> EnqueueAsync(OutputItem item, CancellationToken ct = default) => ValueTask.FromResult(1L);

        public async IAsyncEnumerable<OutputItem> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task AckAsync(long sequence, CancellationToken ct = default)
        {
            Acked.Add(sequence);
            return Task.CompletedTask;
        }

        public Task FailAsync(long sequence, bool permanent, CancellationToken ct = default)
        {
            Failed.Add((sequence, permanent));
            return Task.CompletedTask;
        }

        public IReadOnlyList<OutputItem> ReadSince(string? conversationId, long sinceSequence) => [];
    }

    [Fact]
    public async Task 回合可被取消_以cancelled收尾()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var runtime = new SessionfulStubRuntime { StreamDelay = TimeSpan.FromSeconds(30) };
        var run = HubServer.RunAsync(runtime, new HubServerOptions { Listen = $"http://127.0.0.1:{port}" }, ct: cts.Token);

        var client = await ConnectWithRetryAsync($"ws://127.0.0.1:{port}/v1/stream", cts.Token);
        try
        {
            _ = await OpenSessionAsync(client, "builtin-rin", "companion", "main", cts.Token);
            const string turnId = "turn_cancel_target";

            await client.SendAsync(EnvelopeFactory.Create(MessageTypes.TurnSubmit,
                new TurnRequestDto { TurnId = turnId, Text = "long" }), cts.Token);

            // 登记发生在 turn.accepted 之前，故收到 accepted 后取消必定命中
            while (true)
            {
                var envelope = await client.ReceiveAsync(cts.Token) ?? throw new IOException("hub closed the connection");
                if (envelope.Type == EventTypes.TurnAccepted)
                    break;
            }

            await client.SendAsync(EnvelopeFactory.Create(MessageTypes.TurnCancel,
                new TurnCancelCommand { TurnId = turnId }), cts.Token);

            while (true)
            {
                var envelope = await client.ReceiveAsync(cts.Token) ?? throw new IOException("hub closed the connection");
                if (envelope.Type == EventTypes.Error)
                    Assert.Fail($"cancel path returned error: {envelope.ErrorCode} {envelope.ErrorMsg}");
                if (envelope.Type != EventTypes.TurnFinal)
                    continue;

                var final = envelope.DataAs<TurnResultDto>() ?? throw new InvalidOperationException("no turn.final payload");
                Assert.False(final.Success);
                Assert.Equal(TerminationReasons.Cancelled, final.TerminationReason);
                break;
            }
        }
        finally
        {
            await client.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { /* 取消退出 */ }
        }
    }

    [Fact]
    public async Task presence订阅_可按会话过滤()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var run = HubServer.RunAsync(new SessionfulStubRuntime(), new HubServerOptions { Listen = $"http://127.0.0.1:{port}" }, ct: cts.Token);
        var url = $"ws://127.0.0.1:{port}/v1/stream";

        var a = await ConnectWithRetryAsync(url, cts.Token, "client-a");
        var b = await ConnectWithRetryAsync(url, cts.Token, "client-b");
        var c = await ConnectWithRetryAsync(url, cts.Token, "client-c");
        try
        {
            var main = await OpenSessionAsync(a, "builtin-rin", "companion", "main", cts.Token);
            await a.SendAsync(EnvelopeFactory.Create(MessageTypes.PresenceSubscribe,
                new PresenceSubscription { SessionIds = [main.SessionId] }), cts.Token);
            // 订阅时刻的在场快照里只会含 A 自己（client-a）；下面的匹配按 clientId 过滤，无需另做丢弃

            // B 进入同一段对话（同 conversation → 复用同一 session）→ A 应看到 B
            var bSession = await OpenSessionAsync(b, "builtin-rin", "companion", "main", cts.Token);
            Assert.Equal(main.SessionId, bSession.SessionId);
            Assert.Equal("client-b", await WaitPresenceFromAsync(a, "client-b", cts.Token));

            // C 进入另一段对话 → A 的过滤不应让它看到 C
            _ = await OpenSessionAsync(c, "builtin-rin", "companion", "other", cts.Token);
            Assert.Null(await WaitPresenceFromAsync(a, "client-c", cts.Token, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            await a.DisposeAsync();
            await b.DisposeAsync();
            await c.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { /* 取消退出 */ }
        }
    }

    /// <summary>
    /// 在时限内读取在场事件，返回首个来自 <paramref name="clientId"/> 的（未出现则 null）。
    /// 注意：**不能**给 <c>ReceiveAsync</c> 传超时 token——取消接收会把 ClientWebSocket 整个 abort
    /// （.NET 的行为），连接会直接断。这里改为"一次接收 + 按窗口 WhenAny"，未到达就放弃等待。
    /// </summary>
    private static async Task<string?> WaitPresenceFromAsync(
        HubClient client, string clientId, CancellationToken ct, TimeSpan? window = null)
    {
        var deadline = DateTime.UtcNow + (window ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var read = client.ReceiveAsync(ct);
            var completed = await Task.WhenAny(read, Task.Delay(remaining, ct));
            if (completed != read)
                break;   // 窗口用完

            var envelope = await read;
            if (envelope is null)
                throw new IOException("hub closed the connection");
            if (envelope.Type == EventTypes.PresenceUpdated && envelope.DataAs<PresenceUpdatedEvent>()?.ClientId == clientId)
                return clientId;
        }

        return null;
    }

    [Fact]
    public async Task turn_request的身份与线程字段_落到内核()
    {
        // D45 守卫：`turn.request` 的 userId/threadId/mentions/meta 曾被 Hub 静默丢弃
        // （桥侧早就把 QQ 号发上来了）。这条测试钉住"必须传进内核"。
        var port = FreePort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var runtime = new SessionfulStubRuntime();
        var run = HubServer.RunAsync(runtime, new HubServerOptions { Listen = $"http://127.0.0.1:{port}" }, ct: cts.Token);

        var client = await ConnectWithRetryAsync($"ws://127.0.0.1:{port}/v1/stream", cts.Token);
        try
        {
            var session = await OpenSessionAsync(client, "builtin-rin", "companion", "qq-group-1", cts.Token);

            await client.SendAsync(EnvelopeFactory.Create(MessageTypes.TurnSubmit, new TurnRequestDto
            {
                Text = "hi",
                UserId = "10001",
                ThreadId = "topic-7",
                Mentions = ["rin"],
                Meta = new Dictionary<string, JsonNode?> { ["scene"] = "group" },
            }), cts.Token);

            var sawFinal = false;
            while (!sawFinal)
            {
                var evt = await client.ReceiveAsync(cts.Token) ?? throw new IOException("hub closed the connection");
                if (evt.Type == EventTypes.TurnFinal)
                    sawFinal = true;
            }

            var request = runtime.LastTurnRequest ?? throw new InvalidOperationException("内核未收到回合请求");
            Assert.Equal("10001", request.UserId);
            Assert.Equal("topic-7", request.ThreadId);
            Assert.Equal(["rin"], request.Mentions);
            Assert.Equal("group", request.Meta!["scene"]!.ToString());

            // 感知通道（群聊"看但不回"）同样要带稳定身份，否则历史归因又退化成显示名
            await client.SendAsync(EnvelopeFactory.Create(MessageTypes.MessageAppend, new MessageAppendCommand
            {
                SessionId = session.SessionId, SenderName = "阿明", UserId = "10002", Text = "群聊闲聊",
            }), cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (runtime.LastAppend is null && DateTime.UtcNow < deadline)
                await Task.Delay(50, cts.Token);

            var append = runtime.LastAppend ?? throw new InvalidOperationException("内核未收到感知消息");
            Assert.Equal("10002", append.UserId);
            Assert.Equal("阿明", append.SenderName);
        }
        finally
        {
            await client.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { /* 取消退出 */ }
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

        /// <summary>回合流的延迟（&gt;0 时用于验证 turn.cancel：延迟期间取消会抛 OperationCanceledException）。</summary>
        public TimeSpan StreamDelay { get; set; } = TimeSpan.Zero;

        /// <summary>最近一次收到的内核回合请求（守护 wire → 内核 的身份/线程映射，别再把它们丢掉）。</summary>
        public TurnRequest? LastTurnRequest { get; private set; }

        /// <summary>最近一次感知消息写入（守护 message.append 的身份映射）。</summary>
        public (string SessionId, string? SenderName, string? UserId, string? ThreadId)? LastAppend { get; private set; }

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

        /// <summary>只产出一个 final（供"回合后状态推送"测试走完回合流程）。</summary>
        public async IAsyncEnumerable<TurnEvent> StreamTurnAsync(
            TurnRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            LastTurnRequest = request;
            if (StreamDelay > TimeSpan.Zero)
                await Task.Delay(StreamDelay, ct);   // 取消时在这里抛出 → 验证 turn.cancel
            else
                await Task.Yield();
            yield return new TurnEvent
            {
                TurnId = request.TurnId,
                Kind = "final",
                Payload = new TurnResult
                {
                    Success = true,
                    FinalText = "stub",
                    TerminationReason = TerminationReason.FinalReply,
                },
            };
        }
        public Task AppendMessageAsync(string sessionId, string? senderName, string text,
            IReadOnlyList<object>? blocks = null, string? userId = null, string? threadId = null,
            CancellationToken ct = default)
        {
            LastAppend = (sessionId, senderName, userId, threadId);
            return Task.CompletedTask;
        }
        public Task<string?> GetContextSummaryAsync(string sessionId, CancellationToken ct = default) => throw new NotSupportedException();

        private static string Key(string subjectId, string agentId, string conversationId) => $"{subjectId}\u001f{agentId}\u001f{conversationId}";
    }
}
