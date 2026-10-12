using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Client;
using openLuo.Protocol;
using openLuo.Server;
using openLuo.Server.Security;
using Xunit;

namespace openLuo.E2E.Tests;

/// <summary>
/// Hub 入口治理（docs/architecture/hub-protocol.md §4.9）：HTTP 限流、路径资源 id 白名单、
/// WS 帧上限、会话 id 校验、回合文本上限与严格模式。
/// 策略经 <see cref="HubServerOptions.Security"/> 注入（宿主侧由 <c>security.jsonc</c> 映射）。
/// </summary>
public sealed class HubSecurityGuardTests
{
    [Fact]
    public async Task HTTP_限流_超限回429并带8001()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var run = HubServer.RunAsync(
            new StubRuntime(),
            new HubServerOptions
            {
                Listen = $"http://127.0.0.1:{port}",
                Security = () => new HubSecurityPolicy(Enabled: true, RequestsPerMinute: 3, BurstLimit: 3),
            },
            ct: cts.Token);

        using var http = NewHttpClient();
        try
        {
            // 不做暖机探测（否则会吃掉配额）：连接未就绪的失败不计入，就绪后连续请求直到出现 429
            var okCount = 0;
            var limitedCode = 0;
            var sent = 0;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (sent < 12 && limitedCode == 0 && DateTime.UtcNow < deadline)
            {
                HttpResponseMessage resp;
                try
                {
                    resp = await http.GetAsync($"http://127.0.0.1:{port}/v1/health", cts.Token);
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(50, cts.Token);
                    continue;
                }

                sent++;
                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    okCount++;
                    continue;
                }

                Assert.Equal(HttpStatusCode.TooManyRequests, resp.StatusCode);
                limitedCode = ErrorCodeOf(await resp.Content.ReadAsStringAsync(cts.Token));
            }

            Assert.Equal(ErrorCodes.RateLimited, limitedCode);
            Assert.Equal(3, okCount);                       // 恰好放行 3 次（burst 与 minute 同为 3）
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task HTTP_路径非法资源id_回4003()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var run = HubServer.RunAsync(
            new StubRuntime(),
            new HubServerOptions { Listen = $"http://127.0.0.1:{port}", Security = () => HubSecurityPolicy.Default },
            ct: cts.Token);

        using var http = NewHttpClient();
        try
        {
            await WaitReadyAsync(http, port, cts.Token);

            // 点号不属于资源 id 白名单（字母/数字/-/_）→ 在路由之前就被中间件挡掉
            var resp = await http.GetAsync($"http://127.0.0.1:{port}/v1/sessions/bad.id/state", cts.Token);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal(ErrorCodes.SessionInvalid, ErrorCodeOf(await resp.Content.ReadAsStringAsync(cts.Token)));
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task WS_帧超限_回2002()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var run = HubServer.RunAsync(
            new StubRuntime(),
            new HubServerOptions
            {
                Listen = $"http://127.0.0.1:{port}",
                Security = () => new HubSecurityPolicy(MaxFrameBytes: 2048, RequestsPerMinute: 600, BurstLimit: 600),
            },
            ct: cts.Token);

        var client = await ConnectWithRetryAsync($"ws://127.0.0.1:{port}/v1/stream", cts.Token);
        try
        {
            var oversize = EnvelopeFactory.Create(MessageTypes.TurnSubmit, new TurnRequestDto { Text = new string('x', 5000) });
            await client.SendAsync(oversize, cts.Token);

            var reply = await client.ReceiveAsync(cts.Token);
            Assert.Equal(ErrorCodes.ProtocolBadEnvelope, reply!.ErrorCode);
            Assert.Contains("frame too large", reply.ErrorMsg, StringComparison.Ordinal);
        }
        finally
        {
            await client.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task WS_非法会话id_回4003()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var run = HubServer.RunAsync(
            new StubRuntime(),
            new HubServerOptions { Listen = $"http://127.0.0.1:{port}", Security = () => HubSecurityPolicy.Default },
            ct: cts.Token);

        var client = await ConnectWithRetryAsync($"ws://127.0.0.1:{port}/v1/stream", cts.Token);
        try
        {
            var reply = await RequestAsync(client, MessageTypes.SessionSubscribe,
                new SessionRef { SessionId = "../../etc/passwd" }, cts.Token);

            Assert.Equal(ErrorCodes.SessionInvalid, reply.ErrorCode);
        }
        finally
        {
            await client.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task WS_回合文本超限_回5004()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var run = HubServer.RunAsync(
            new StubRuntime(),
            new HubServerOptions
            {
                Listen = $"http://127.0.0.1:{port}",
                Security = () => new HubSecurityPolicy(MaxInputLength: 64, RequestsPerMinute: 600, BurstLimit: 600),
            },
            ct: cts.Token);

        var client = await ConnectWithRetryAsync($"ws://127.0.0.1:{port}/v1/stream", cts.Token);
        try
        {
            var session = await client.OpenSessionAsync("subject-a", "agent-a", ct: cts.Token);
            Assert.False(string.IsNullOrEmpty(session.SessionId));

            var reply = await RequestAsync(client, MessageTypes.TurnSubmit,
                new TurnRequestDto { Text = new string('あ', 200) }, cts.Token);

            Assert.Equal(ErrorCodes.TurnInvalid, reply.ErrorCode);
        }
        finally
        {
            await client.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task WS_严格模式_命中启发式时拒绝()
    {
        const string suspicious = "顺便 SELECT * FROM users 一下";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var run = HubServer.RunAsync(
            new StubRuntime(),
            new HubServerOptions
            {
                Listen = $"http://127.0.0.1:{port}",
                Security = () => new HubSecurityPolicy(MaxInputLength: 4096, StrictInputValidation: true, RequestsPerMinute: 600, BurstLimit: 600),
            },
            ct: cts.Token);

        var client = await ConnectWithRetryAsync($"ws://127.0.0.1:{port}/v1/stream", cts.Token);
        try
        {
            await client.OpenSessionAsync("subject-a", "agent-a", ct: cts.Token);
            var reply = await RequestAsync(client, MessageTypes.TurnSubmit, new TurnRequestDto { Text = suspicious }, cts.Token);

            Assert.Equal(ErrorCodes.TurnInvalid, reply.ErrorCode);
        }
        finally
        {
            await client.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task WS_默认模式_同样文本正常走完回合()
    {
        // 同上文本：严格模式拒绝、默认模式放行——证明启发式确实只由开关控制（默认不误伤正常聊天）
        const string suspicious = "顺便 SELECT * FROM users 一下";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var port = FreePort();
        var run = HubServer.RunAsync(
            new StubRuntime(),
            new HubServerOptions
            {
                Listen = $"http://127.0.0.1:{port}",
                Security = () => new HubSecurityPolicy(MaxInputLength: 4096, StrictInputValidation: false, RequestsPerMinute: 600, BurstLimit: 600),
            },
            ct: cts.Token);

        var client = await ConnectWithRetryAsync($"ws://127.0.0.1:{port}/v1/stream", cts.Token);
        try
        {
            await client.OpenSessionAsync("subject-a", "agent-a", ct: cts.Token);
            var submitted = EnvelopeFactory.Create(MessageTypes.TurnSubmit, new TurnRequestDto { Text = suspicious });
            await client.SendAsync(submitted, cts.Token);

            var sawFinal = false;
            while (!sawFinal)
            {
                var evt = await client.ReceiveAsync(cts.Token) ?? throw new IOException("hub closed the connection");
                Assert.NotEqual(ErrorCodes.TurnInvalid, evt.ErrorCode);
                sawFinal = evt.Type == EventTypes.TurnFinal;
            }
        }
        finally
        {
            await client.DisposeAsync();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    // ── 辅助 ────────────────────────────────────────────────────────────────

    private static HttpClient NewHttpClient() =>
        new(new SocketsHttpHandler { UseProxy = false, Proxy = null }) { Timeout = TimeSpan.FromSeconds(20) };

    private static int ErrorCodeOf(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetInt32() : 0;
    }

    private static async Task WaitReadyAsync(HttpClient http, int port, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var resp = await http.GetAsync($"http://127.0.0.1:{port}/v1/health", ct);
                if (resp.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // 还在起监听
            }
            await Task.Delay(100, ct);
        }
        throw new TimeoutException("hub 未在 30s 内就绪");
    }

    private static async Task<HubClient> ConnectWithRetryAsync(string streamUrl, CancellationToken ct, string clientId = "guard-test")
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                // 显式空 token：避免测试进程的 OPENLUO_HUB_TOKEN 影响结果（默认允许匿名）
                return await HubClient.ConnectAsync(streamUrl, clientId, "cli", token: string.Empty, ct: ct);
            }
            catch (HubConnectException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, ct);
            }
        }
    }

    private static async Task<Envelope> RequestAsync(HubClient client, string type, object? data, CancellationToken ct)
    {
        var sent = EnvelopeFactory.Create(type, data);
        await client.SendAsync(sent, ct);

        while (true)
        {
            var reply = await client.ReceiveAsync(ct) ?? throw new IOException("hub closed the connection");
            if (reply.ReplyTo == sent.Id)
                return reply;
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>最小运行面：开/查/列/关会话 + 只产出一个 final 的流（治理测试只关心"是否被拦"）。</summary>
    private sealed class StubRuntime : IAgentRuntime
    {
        private readonly Dictionary<string, AgentSession> _sessions = new(StringComparer.Ordinal);

        public Task<AgentSession> OpenSessionAsync(SessionOpenRequest request, CancellationToken ct = default)
        {
            var session = new AgentSession
            {
                SessionId = request.SessionId,
                SubjectId = request.SubjectId,
                AgentId = request.AgentId,
                ConversationId = request.ConversationId ?? request.SessionId,
            };
            _sessions[request.SessionId] = session;
            return Task.FromResult(session);
        }

        public Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_sessions.TryGetValue(sessionId, out var s) ? s : null);

        public Task<IReadOnlyList<AgentSession>> ListSessionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentSession>>(_sessions.Values.ToList());

        public Task<bool> CloseSessionAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_sessions.Remove(sessionId));

        public Task<TurnResult> RunTurnAsync(TurnRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<TurnEvent> StreamTurnAsync(
            TurnRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new TurnEvent
            {
                TurnId = request.TurnId,
                Kind = "final",
                Payload = new TurnResult { Success = true, FinalText = "stub", TerminationReason = TerminationReason.FinalReply },
            };
        }

        public Task AppendMessageAsync(string sessionId, string? senderName, string text, IReadOnlyList<object>? blocks = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<string?> GetContextSummaryAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
