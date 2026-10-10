using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using openLuo.Client;
using openLuo.Interfaces.QQbot;
using openLuo.Protocol;
using Xunit;

namespace openLuo.Cli.Tests;

/// <summary>
/// QQ 桥的 Hub 连接韧性：Hub 中途重启（连接被硬断）后，桥必须**自动重连**，后续消息照常得到回复。
/// 修复前的表现：连接一断就永久失效——每条消息都报
/// <c>message handler failed: The WebSocket is in an invalid state ('Aborted') ...</c>，且永不重连（只能重启进程）。
/// </summary>
public sealed class QqBridgeReconnectTests
{
    private sealed class FixedConfigCenter(QqBotConfig config) : IQqBotConfigCenter
    {
        public QqBotConfig GetSnapshot() => config.Clone();
    }

    [Fact]
    public async Task Bridge_reconnects_when_hub_connection_is_killed()
    {
        var replies = new ConcurrentQueue<string>();
        var turns = 0;

        await using var hub = new MiniWsServer(async (connection, json) =>
        {
            var root = JsonNode.Parse(json)!.AsObject();
            Console.Error.WriteLine($"[hub-stub] {json}");
            switch (root["type"]?.GetValue<string>())
            {
                case MessageTypes.Hello:
                    await connection.SendTextAsync(ProtocolJson.Serialize(
                        EnvelopeFactory.Create(EventTypes.Welcome, new WelcomeEvent { ServerVersion = "test" })));
                    break;
                case MessageTypes.SessionOpen:
                    await connection.SendTextAsync(ProtocolJson.Serialize(
                        EnvelopeFactory.Create(EventTypes.SessionOpened, new SessionDto { SessionId = "s-qq" })));
                    break;
                case MessageTypes.TurnSubmit:
                    var n = Interlocked.Increment(ref turns);
                    await connection.SendTextAsync(ProtocolJson.Serialize(
                        EnvelopeFactory.Create(EventTypes.TurnFinal, new TurnResultDto
                        {
                            TurnId = $"t{n}", Success = true, FinalText = $"reply-{n}"
                        })));
                    break;
            }
        });

        await using var onebot = new MiniWsServer(async (connection, json) =>
        {
            var root = JsonNode.Parse(json)!.AsObject();
            Console.Error.WriteLine($"[onebot-stub] {json}");
            var echo = root["echo"]?.GetValue<string>() ?? string.Empty;
            switch (root["action"]?.GetValue<string>())
            {
                case "get_login_info":
                    await connection.SendTextAsync(ApiOk(echo, """{"user_id":999,"nickname":"bot"}"""));
                    break;
                case "send_private_msg":
                    replies.Enqueue(root["params"]?["message"]?[0]?["data"]?["text"]?.GetValue<string>() ?? string.Empty);
                    await connection.SendTextAsync(ApiOk(echo, """{"message_id":1}"""));
                    break;
            }
        });

        var config = new QqBotConfig
        {
            Enabled = true,
            BaseAddress = onebot.StreamUrl,
            TargetFriendIds = [10001],
            LogMessages = false,
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var app = new QqBotApplication(
            ct => HubClient.ConnectAsync(hub.StreamUrl, "qq-bridge", "qq", ct: ct),
            http, $"http://127.0.0.1:{hub.Port}", new FixedConfigCenter(config));
        var run = app.RunAsync(cts.Token);

        try
        {
            var bot = await onebot.WaitForConnectionAsync(TimeSpan.FromSeconds(10));
            await WaitForAsync(() => hub.ConnectionCount >= 1, TimeSpan.FromSeconds(10));

            await bot.SendTextAsync(PrivateMessage("你好", userId: 10001, selfId: 999));
            Assert.Equal("reply-1", await NextReplyAsync(replies, TimeSpan.FromSeconds(20)));

            // Hub 连接被硬断（等价于 Hub 重启 / 链路被掐）
            hub.AbortAll();

            await bot.SendTextAsync(PrivateMessage("再说一句", userId: 10001, selfId: 999));
            Assert.Equal("reply-2", await NextReplyAsync(replies, TimeSpan.FromSeconds(30)));
            Assert.True(hub.ConnectionCount >= 2, "断连后桥应重连到 Hub");
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { /* 取消退出 */ }
        }
    }

    private static string ApiOk(string echo, string data) =>
        $$"""{"status":"ok","retcode":0,"data":{{data}},"echo":"{{echo}}"}""";

    private static string PrivateMessage(string text, long userId, long selfId) =>
        new JsonObject
        {
            ["post_type"] = "message",
            ["message_type"] = "private",
            ["sub_type"] = "friend",
            ["message_id"] = 1,
            ["user_id"] = userId,
            ["self_id"] = selfId,
            ["raw_message"] = text,
            ["message"] = new JsonArray(new JsonObject { ["type"] = "text", ["data"] = new JsonObject { ["text"] = text } }),
            ["sender"] = new JsonObject { ["nickname"] = "Tester" },
        }.ToJsonString();

    private static async Task<string> NextReplyAsync(ConcurrentQueue<string> queue, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var reply))
                return reply;
            await Task.Delay(20);
        }
        throw new TimeoutException("OneBot 未收到回复");
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }
        throw new TimeoutException("等待条件超时");
    }
}
