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
            new UnusedRuntime(),
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

    /// <summary>本测试只发协议命令、不触达内核：任何成员被调用都说明 Hub 启动期意外依赖了 runtime。</summary>
    private sealed class UnusedRuntime : IAgentRuntime
    {
        public Task<AgentSession> OpenSessionAsync(SessionOpenRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentSession>> ListSessionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> CloseSessionAsync(string sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TurnResult> RunTurnAsync(TurnRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TurnEvent> StreamTurnAsync(TurnRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendMessageAsync(string sessionId, string? senderName, string text, IReadOnlyList<object>? blocks = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetContextSummaryAsync(string sessionId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
