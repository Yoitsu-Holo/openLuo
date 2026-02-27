using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.SessionRuntime.Application;

namespace openLuo.Agent.Tests;

public sealed class CharacterAgentFlowTests
{
    [Fact]
    public async Task RunTurnAsync_DelegatesToFlowRunner()
    {
        var memoryGateway = Substitute.For<ICharacterMemoryGateway>();
        var stateGateway = Substitute.For<ICharacterStateGateway>();
        var capabilityProvider = Substitute.For<ICharacterCapabilitySnapshotProvider>();
        var promptBuilder = Substitute.For<ICharacterPromptContextBuilder>();
        var flowRunner = Substitute.For<IAgentFlowRunner>();
        var emitterFactory = Substitute.For<ITurnMessageEmitterFactory>();
        emitterFactory.Create(Arg.Any<AgentFlowRunRequest>()).Returns(NullTurnMessageEmitter.Instance);

        var request = new CharacterTurnRequest
        {
            Context = new AgentContext { GameId = "g1", CharacterId = "c1" },
            Profile = new AgentProfile { CharacterId = "c1", DisplayName = "铃", RolePrompt = "角色：铃" },
            Message = new AgentMessage("m1", "g1", "player", "c1", AgentMessageType.Chat, "你好", "chat1", DateTimeOffset.UtcNow),
            Memory = new CharacterMemorySnapshot()
        };

        stateGateway.BuildStateSummaryAsync(request, Arg.Any<CancellationToken>()).Returns("affection=50");
        capabilityProvider.LoadAsync(request, Arg.Any<CancellationToken>()).Returns(new AgentCapabilitySnapshot());
        promptBuilder.BuildAsync(request, request.Memory, Arg.Any<AgentCapabilitySnapshot>(), "affection=50", Arg.Any<CancellationToken>())
            .Returns(new CharacterPromptContext { CharacterProfile = "角色：铃", PlayerInput = "你好" });
        flowRunner.RunAsync(Arg.Any<AgentFlowRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(AgentFlowRunResult.Ok("done", new Dictionary<string, object?>
            {
                ["turnResult"] = new CharacterTurnResult { Reply = "你好。" }
            }, []));

        var sut = new CharacterAgent(memoryGateway, stateGateway, capabilityProvider, promptBuilder, flowRunner, emitterFactory);

        var result = await sut.RunTurnAsync(request);

        Assert.Equal("你好。", result.Reply);
        await flowRunner.Received(1).RunAsync(
            Arg.Is<AgentFlowRunRequest>(x =>
                x.FlowId == CharacterStandardChatFlow.FlowId &&
                x.AgentId == "c1" &&
                x.GameId == "g1" &&
                x.Inputs.ContainsKey("turnContext")),
            Arg.Any<CancellationToken>());
    }
}
