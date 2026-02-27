using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces;

namespace openLuo.Agent.Tests;

public sealed class DefaultAgentMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_DelegatesToCharacterTurnChain()
    {
        var builder = Substitute.For<ICharacterTurnRequestBuilder>();
        var characterAgent = Substitute.For<ICharacterAgent>();
        var applier = Substitute.For<ICharacterTurnResultApplier>();
        var context = new AgentContext
        {
            GameId = "g1",
            CharacterId = "c1"
        };
        var message = new AgentMessage(
            MessageId: "m1",
            GameId: "g1",
            From: "player",
            To: "c1",
            Type: AgentMessageType.Chat,
            Payload: "你好",
            CorrelationId: "chat1",
            TimestampUtc: DateTimeOffset.UtcNow);
        var request = new CharacterTurnRequest
        {
            Context = context,
            Profile = new AgentProfile { CharacterId = "c1", DisplayName = "铃" },
            Message = message,
            Memory = new CharacterMemorySnapshot()
        };
        var turnResult = new CharacterTurnResult
        {
            Reply = "你好。"
        };
        var reply = new AgentMessage(
            MessageId: "m2",
            GameId: "g1",
            From: "c1",
            To: "player",
            Type: AgentMessageType.Chat,
            Payload: "你好。",
            CorrelationId: "chat1",
            TimestampUtc: DateTimeOffset.UtcNow);

        builder.BuildAsync(context, message, Arg.Any<CancellationToken>()).Returns(request);
        characterAgent.RunTurnAsync(request, Arg.Any<CancellationToken>()).Returns(turnResult);
        applier.ApplyAsync(context, message, turnResult, Arg.Any<CancellationToken>()).Returns(reply);

        var sut = new DefaultAgentMessageHandler(builder, characterAgent, applier);

        var result = await sut.HandleAsync(context, message, CancellationToken.None);

        Assert.Same(reply, result);
        await builder.Received(1).BuildAsync(context, message, Arg.Any<CancellationToken>());
        await characterAgent.Received(1).RunTurnAsync(request, Arg.Any<CancellationToken>());
        await applier.Received(1).ApplyAsync(context, message, turnResult, Arg.Any<CancellationToken>());
    }
}
