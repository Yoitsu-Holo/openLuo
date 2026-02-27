using openLuo.Core.Interfaces;
using openLuo.Modules.Agent.Application;

namespace openLuo.Agent.Tests;

public sealed class CharacterAgentRuntimeTimeoutTests
{
    [Fact]
    public async Task RequestTimeout_DoesNotKillRuntime_AndInFlightWorkIsCanceled()
    {
        var dispatcher = new AgentDispatcher();
        var mailbox = new ChannelAgentMailbox("c1");
        var handler = new TimeoutAwareHandler();
        var contextStore = new InMemoryAgentContextStore();
        var logger = Substitute.For<IGameLogger>();
        var runtime = new CharacterAgentRuntime(mailbox, handler, contextStore, logger);

        await dispatcher.RegisterAsync("c1", mailbox);
        await runtime.StartAsync();

        try
        {
            var slowResult = await dispatcher.RequestAsync(
                new AgentMessage(
                    MessageId: "m1",
                    GameId: "g1",
                    From: "player",
                    To: "c1",
                    Type: AgentMessageType.Chat,
                    Payload: "slow",
                    CorrelationId: null,
                    TimestampUtc: DateTimeOffset.UtcNow),
                TimeSpan.FromMilliseconds(50),
                CancellationToken.None);

            var fastResult = await dispatcher.RequestAsync(
                new AgentMessage(
                    MessageId: "m2",
                    GameId: "g1",
                    From: "player",
                    To: "c1",
                    Type: AgentMessageType.Chat,
                    Payload: "fast",
                    CorrelationId: null,
                    TimestampUtc: DateTimeOffset.UtcNow),
                TimeSpan.FromSeconds(1),
                CancellationToken.None);

            Assert.Null(slowResult);
            Assert.True(handler.SlowCanceled);
            Assert.NotNull(fastResult);
            Assert.Equal("ok", fastResult!.Payload);
        }
        finally
        {
            await runtime.StopAsync();
            await dispatcher.UnregisterAsync("c1");
        }
    }

    private sealed class TimeoutAwareHandler : IAgentMessageHandler
    {
        public bool SlowCanceled { get; private set; }

        public async Task<AgentMessage?> HandleAsync(AgentContext context, AgentMessage message, CancellationToken ct = default)
        {
            if (message.Payload == "slow")
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                catch (OperationCanceledException)
                {
                    SlowCanceled = true;
                    throw;
                }
            }

            return new AgentMessage(
                MessageId: Guid.NewGuid().ToString("N"),
                GameId: message.GameId,
                From: context.CharacterId,
                To: message.From,
                Type: AgentMessageType.Chat,
                Payload: "ok",
                CorrelationId: message.CorrelationId,
                TimestampUtc: DateTimeOffset.UtcNow);
        }
    }
}
