using openLuo.Modules.Agent.Application;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using NSubstitute;

namespace openLuo.Agent.Tests;

public sealed class CharacterPromptContextBuilderTests
{
    [Fact]
    public async Task Build_SplitsWorldSceneAndGoal_AndDoesNotDuplicateCurrentUserMessage()
    {
        var builder = new CharacterPromptContextBuilder(Substitute.For<ITimeService>());
        var request = new CharacterTurnRequest
        {
            Context = new AgentContext
            {
                GameId = "g1",
                CharacterId = "c1",
                Summary = "affection=50",
                Conversation =
                {
                    new AgentConversationTurn
                    {
                        SpeakerId = "player",
                        SpeakerRole = "inbound",
                        Content = "之前你说艾莉娅可能知道。"
                    },
                    new AgentConversationTurn
                    {
                        SpeakerId = "c1",
                        SpeakerRole = "outbound",
                        Content = "是的，她更熟悉宅邸收纳。"
                    }
                }
            },
            Profile = new AgentProfile
            {
                CharacterId = "c1",
                DisplayName = "铃",
                RolePrompt = "角色：铃"
            },
            Message = new AgentMessage("m1", "g1", "player", "c1", AgentMessageType.Chat, "帮我问问她。", "chat1", DateTimeOffset.UtcNow),
            Memory = new CharacterMemorySnapshot(),
            ExtraContexts =
            [
                new AgentContextBlock(EnhanceMessageRule.WorldContext, "世界观：架空宅邸日常互动场景。"),
                new AgentContextBlock(EnhanceMessageRule.SceneState, "场景：傍晚，宅邸客厅。"),
                new AgentContextBlock(EnhanceMessageRule.GoalContext, "目标：如果角色不确定，就使用 ask_character。")
            ]
        };

        var result = await builder.BuildAsync(
            request,
            new CharacterMemorySnapshot(),
            new AgentCapabilitySnapshot(),
            "affection=50");

        Assert.Equal("世界观：架空宅邸日常互动场景。", result.WorldContext);
        Assert.Equal("场景：傍晚，宅邸客厅。", result.SceneState);
        Assert.Equal("目标：如果角色不确定，就使用 ask_character。", result.GoalContext);
        Assert.Equal("帮我问问她。", result.PlayerInput);
        Assert.Equal(2, result.Conversation.Count);
        Assert.DoesNotContain(result.Conversation, x => x.Content == "帮我问问她。");
    }
}
