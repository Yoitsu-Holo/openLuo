using openLuo.Modules.Agent.Application;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Executor.Application.StateUpdate;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Agent.Tests;

public sealed class CharacterStateUpdateNodeTests
{
    [Fact]
    public async Task ExecuteAsync_IgnoresInterAgentOutcomeAndUsesToolResultsOnly()
    {
        var executor = Substitute.For<IExecutor<StateUpdateInput, StateUpdateOutput>>();
        var config = Substitute.For<IRuntimeConfigCenter>();
        config.GetSnapshot().Returns(new AppConfig());
                config.GetSnapshot().Returns(new AppConfig());
        StateUpdateInput? captured = null;
        executor.ExecuteAsync(Arg.Any<StateUpdateInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<StateUpdateInput>();
                return ExecutorResult<StateUpdateOutput>.Ok(new StateUpdateOutput());
            });

        var sut = new CharacterStateUpdateNode(executor, config);

        await sut.ExecuteAsync(
            BuildContext(),
            "我去问过艾莉娅了，钥匙在玄关抽屉里。",
            new CharacterToolCallResult
            {
                Reply = "来自 艾莉娅 的回复：\n钥匙在玄关抽屉里。",
                InterAgentOutcome = new InterAgentOutcome
                {
                    ReplyText = "钥匙在玄关抽屉里。"
                }
            });

        Assert.NotNull(captured);
        Assert.Contains(captured!.ToolResults, x => x.Contains("来自 艾莉娅 的回复"));
    }

    private static CharacterTurnContext BuildContext() => new()
    {
        PromptContext = new CharacterPromptContext
        {
            SceneState = "场景：宅邸",
            PlayerInput = "帮我问一下艾莉娅。"
        },
        CurrentStateSummary = "trust=74",
        Request = new CharacterTurnRequest
        {
            Context = new AgentContext
            {
                GameId = "g1",
                CharacterId = "rin"
            },
            Profile = new AgentProfile
            {
                CharacterId = "rin",
                DisplayName = "汐泠"
            },
            Message = new AgentMessage(
                MessageId: "m1",
                GameId: "g1",
                From: "player",
                To: "rin",
                Type: AgentMessageType.Chat,
                Payload: "帮我问一下艾莉娅。",
                CorrelationId: "c1",
                TimestampUtc: DateTimeOffset.UtcNow),
            Memory = new CharacterMemorySnapshot()
        },
        Memory = new CharacterMemorySnapshot(),
        Profile = new CharacterAgentProfile
        {
            CharacterId = "rin",
            DisplayName = "汐泠"
        },
        State = new CharacterAgentState(),
        CapabilitySnapshot = new AgentCapabilitySnapshot()
    };
}
