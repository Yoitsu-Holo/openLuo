using openLuo.Modules.Agent.Application;

namespace openLuo.Agent.Tests;

public sealed class AgentDispatcherTests
{
    [Fact]
    public async Task RequestAsync_WhenReplyTimesOut_ReturnsNull()
    {
        var dispatcher = new AgentDispatcher();
        var mailbox = new ChannelAgentMailbox("c1");
        await dispatcher.RegisterAsync("c1", mailbox);

        var result = await dispatcher.RequestAsync(
            new AgentMessage(
                MessageId: "m1",
                GameId: "g1",
                From: "player",
                To: "c1",
                Type: AgentMessageType.Chat,
                Payload: "hello",
                CorrelationId: null,
                TimestampUtc: DateTimeOffset.UtcNow),
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None);

        Assert.Null(result);
    }
}
