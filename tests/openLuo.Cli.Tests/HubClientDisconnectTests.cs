using System.Text.Json.Nodes;
using openLuo.Client;
using openLuo.Protocol;
using Xunit;

namespace openLuo.Cli.Tests;

/// <summary>
/// Hub 连接被硬断后的客户端行为：必须是"可识别的断连"（<see cref="HubDisconnectedException"/> + <c>IsConnected=false</c>），
/// 而不是底层 <c>InvalidOperationException: The WebSocket is in an invalid state ('Aborted') ...</c>
/// （此前把断连伪装成"处理消息失败"，误导排查），也不能把断连静默当成回合正常结束。
/// </summary>
public sealed class HubClientDisconnectTests
{
    private static string Welcome() =>
        ProtocolJson.Serialize(EnvelopeFactory.Create(EventTypes.Welcome, new WelcomeEvent { ServerVersion = "test" }));

    private static Envelope SomeCommand() =>
        EnvelopeFactory.Create(MessageTypes.MessageAppend, new MessageAppendCommand { SessionId = "s", Text = "t" });

    [Fact]
    public async Task Aborted_connection_surfaces_as_hub_disconnected_on_read_and_write()
    {
        await using var hub = new MiniWsServer(async (connection, json) =>
        {
            if (JsonNode.Parse(json)?["type"]?.GetValue<string>() == MessageTypes.Hello)
                await connection.SendTextAsync(Welcome());
        });

        await using var client = await HubClient.ConnectAsync(hub.StreamUrl, "test-client", "cli");
        Assert.True(client.IsConnected);

        hub.AbortAll();
        await hub.WaitForConnectionAsync(TimeSpan.FromSeconds(5));

        // ① 读取：抛断连异常（而非返回 null 让上层以为回合正常结束、静默丢消息）
        await Assert.ThrowsAsync<HubDisconnectedException>(() => client.ReceiveAsync());
        Assert.False(client.IsConnected);

        // ② 写入：抛断连异常（而非底层 InvalidOperationException），并带上目标地址便于定位
        var error = await Assert.ThrowsAsync<HubDisconnectedException>(() => client.SendAsync(SomeCommand()));
        Assert.Contains("Hub 连接已断开", error.Message);
        Assert.Contains(hub.StreamUrl, error.Message);
    }

    [Fact]
    public async Task Clean_close_frame_ends_read_with_null_and_marks_disconnected()
    {
        await using var hub = new MiniWsServer(async (connection, json) =>
        {
            if (JsonNode.Parse(json)?["type"]?.GetValue<string>() == MessageTypes.Hello)
            {
                await connection.SendTextAsync(Welcome());
                await connection.SendCloseAsync();
            }
        });

        await using var client = await HubClient.ConnectAsync(hub.StreamUrl, "test-client", "cli");

        // 对端正常关闭（带 Close 帧）→ 回合流正常结束：返回 null 而不是抛异常
        Assert.Null(await client.ReceiveAsync());
        Assert.False(client.IsConnected);

        // 之后再用这条连接提交请求：同样是可识别的断连
        await Assert.ThrowsAsync<HubDisconnectedException>(() => client.SendAsync(SomeCommand()));
    }
}
