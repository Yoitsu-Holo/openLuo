using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;

namespace openLuo.Agent.Tests;

public sealed class AgentRuntimeHubTests
{
    [Fact]
    public async Task RequestAsync_StartsRuntime_AndRoutesThroughCharacterHandler()
    {
        var dispatcher = new AgentDispatcher();
        var handler = new EchoHandler();
        var contextStore = new InMemoryAgentContextStore();
        var roster = Substitute.For<IAgentRoster>();
        var logger = Substitute.For<IGameLogger>();
        roster.ListAsync("g1", Arg.Any<CancellationToken>())
            .Returns([new Character { Id = "c2", Name = "艾莉娅", GameId = "g1", ArchetypeId = "bg2" }]);
        var sut = new AgentRuntimeHub(dispatcher, handler, contextStore, roster, logger);

        try
        {
            await sut.EnsurePartyStartedAsync("g1");

            var reply = await sut.RequestAsync(
                characterId: "c2",
                type: AgentMessageType.AgentAsk,
                from: "c1",
                payload: "内部咨询：问题：你的喜好是什么？",
                gameId: "g1",
                correlationId: "ask1",
                timeout: TimeSpan.FromSeconds(1),
                contextBlocks: null);

            Assert.NotNull(reply);
            Assert.Equal("c2", reply!.From);
            Assert.Equal("c1", reply.To);
            Assert.Equal(AgentMessageType.AgentReply, reply.Type);
            Assert.Equal("echo:内部咨询：问题：你的喜好是什么？", reply.Payload);

            var context = await contextStore.GetOrCreateAsync("g1", "c2");
            Assert.Contains(context.Conversation, turn =>
                turn.SpeakerId == "c1" &&
                turn.SpeakerRole == "inbound" &&
                turn.Content.Contains("你的喜好是什么？"));
            Assert.Contains(context.Conversation, turn =>
                turn.SpeakerId == "c2" &&
                turn.SpeakerRole == "outbound" &&
                turn.Content.Contains("echo:"));
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    private sealed class EchoHandler : IAgentMessageHandler
    {
        public Task<AgentMessage?> HandleAsync(AgentContext context, AgentMessage message, CancellationToken ct = default)
        {
            var replyType = message.Type == AgentMessageType.AgentAsk
                ? AgentMessageType.AgentReply
                : AgentMessageType.Chat;

            return Task.FromResult<AgentMessage?>(new AgentMessage(
                MessageId: Guid.NewGuid().ToString("N"),
                GameId: message.GameId,
                From: context.CharacterId,
                To: message.From,
                Type: replyType,
                Payload: $"echo:{message.Payload}",
                CorrelationId: message.CorrelationId,
                TimestampUtc: DateTimeOffset.UtcNow));
        }
    }
}
