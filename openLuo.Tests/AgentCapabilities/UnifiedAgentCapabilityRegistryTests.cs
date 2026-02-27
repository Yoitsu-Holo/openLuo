using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.AgentCapabilities.Application;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Executor.Application.RandomImage;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.InterAgent.Core.Interfaces;

using openLuo.Modules.PluginRuntime.Core.Models;
namespace openLuo.AgentCapabilities.Tests;

public sealed class UnifiedAgentCapabilityRegistryTests
{
    [Fact]
    public async Task BuildSnapshotAsync_ReturnsOnlyInternalAgentCapabilities_AndIncludesKnownCharacters()
    {
        var commandBridge = Substitute.For<IAgentCommandBridge>();
        var roster = Substitute.For<IAgentRoster>();
        roster.ListAsync("g1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new Character { Id = "c1", Name = "铃", ArchetypeId = "bg1" },
                new Character { Id = "c2", Name = "艾莉娅", ArchetypeId = "bg2" }
            ]);

        var sut = new UnifiedAgentCapabilityRegistry(commandBridge, roster);

        var snapshot = await sut.BuildSnapshotAsync(new AgentCapabilityContext
        {
            GameId = "g1",
            CharacterId = "c1"
        });

        Assert.Contains(snapshot.Capabilities, x => x.Name == "list_characters" && x.ExecutorKind == "core");
        Assert.DoesNotContain(snapshot.Capabilities, x => x.Name == "read_file");
        Assert.Equal(2, snapshot.KnownCharacters.Count);
        Assert.Contains(snapshot.KnownCharacters, x => x.DisplayName == "艾莉娅");
    }

    [Fact]
    public async Task ExecuteAsync_ListCharacters_ReturnsRosterText()
    {
        var commandBridge = Substitute.For<IAgentCommandBridge>();
        var roster = Substitute.For<IAgentRoster>();
        var interAgentMessenger = Substitute.For<IInterAgentMessenger>();
        var randomImageFetchExecutor = Substitute.For<IExecutor<RandomImageFetchInput, RandomImageFetchOutput>>();
        roster.ListAsync("g1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new Character { Id = "c1", Name = "铃", ArchetypeId = "bg1" },
                new Character { Id = "c2", Name = "艾莉娅", ArchetypeId = "bg2" }
            ]);

        var sut = new UnifiedAgentCapabilityExecutor(commandBridge, roster, interAgentMessenger, randomImageFetchExecutor);

        var result = await sut.ExecuteAsync(
            new AgentCapabilityDescriptor
            {
                Name = "list_characters",
                Category = "core",
                Prefix = string.Empty,
                ExecutorKind = "core"
            },
            [],
            new Dictionary<string, string>(),
            new AgentCapabilityContext
            {
                GameId = "g1",
                CharacterId = "c1"
            });

        Assert.True(result.Success);
        Assert.Contains("当前可联系角色：", result.Output);
        Assert.Contains("铃 (c1)", result.Output);
        Assert.Contains("艾莉娅 (c2)", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_AskCharacter_UsesRuntimeHubAndReturnsReply()
    {
        var commandBridge = Substitute.For<IAgentCommandBridge>();
        var roster = Substitute.For<IAgentRoster>();
        var interAgentMessenger = Substitute.For<IInterAgentMessenger>();
        var randomImageFetchExecutor = Substitute.For<IExecutor<RandomImageFetchInput, RandomImageFetchOutput>>();

        roster.ResolveAsync("g1", "艾莉娅", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c2", Name = "艾莉娅", ArchetypeId = "bg2" });
        interAgentMessenger.AskAsync(
                Arg.Any<openLuo.Modules.InterAgent.Core.Models.InterAgentAskRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new openLuo.Modules.InterAgent.Core.Models.InterAgentAskResult
            {
                Success = true,
                TargetCharacterId = "c2",
                TargetDisplayName = "艾莉娅",
                Reply = "我喜欢红茶和古典音乐。",
                Outcome = new InterAgentOutcome
                {
                    ReplyText = "我喜欢红茶和古典音乐。"
                }
            });

        var sut = new UnifiedAgentCapabilityExecutor(commandBridge, roster, interAgentMessenger, randomImageFetchExecutor);

        var result = await sut.ExecuteAsync(
            new AgentCapabilityDescriptor
            {
                Name = "ask_character",
                Category = "inter-agent",
                ExecutorKind = "inter-agent"
            },
            [],
            new Dictionary<string, string>
            {
                ["target"] = "艾莉娅",
                ["question"] = "你的喜好是什么？"
            },
            new AgentCapabilityContext
            {
                GameId = "g1",
                CharacterId = "c1"
            });

        Assert.True(result.Success);
        Assert.Contains("来自 艾莉娅 的回复：", result.Output);
        Assert.Contains("我喜欢红茶和古典音乐。", result.Output);
        Assert.True(result.Metadata.TryGetValue(CommandResultMetadataKeys.InterAgentOutcome, out var outcomeObj));
        var outcome = Assert.IsType<InterAgentOutcome>(outcomeObj);
        Assert.Equal("我喜欢红茶和古典音乐。", outcome.ReplyText);
    }

    [Fact]
    public async Task ExecuteAsync_NarrativeChat_DelegatesToPluginChat()
    {
        var commandBridge = Substitute.For<IAgentCommandBridge>();
        var roster = Substitute.For<IAgentRoster>();
        var interAgentMessenger = Substitute.For<IInterAgentMessenger>();
        var randomImageFetchExecutor = Substitute.For<IExecutor<RandomImageFetchInput, RandomImageFetchOutput>>();
        commandBridge.ExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<string[]>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<GameBridgeRequestContext?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(CommandResult.Ok("铃：早上好。"));

        var sut = new UnifiedAgentCapabilityExecutor(commandBridge, roster, interAgentMessenger, randomImageFetchExecutor);

        var result = await sut.ExecuteAsync(
            new AgentCapabilityDescriptor
            {
                Name = "narrative_chat",
                Category = "render",
                ExecutorKind = "core"
            },
            [],
            new Dictionary<string, string> { ["message"] = "早上好" },
            new AgentCapabilityContext
            {
                GameId = "g1",
                CharacterId = "c1"
            });

        Assert.True(result.Success);
        Assert.Equal("铃：早上好。", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ChatWithCharacterSession_ReturnsTranscript()
    {
        var commandBridge = Substitute.For<IAgentCommandBridge>();
        var roster = Substitute.For<IAgentRoster>();
        var interAgentMessenger = Substitute.For<IInterAgentMessenger>();
        var randomImageFetchExecutor = Substitute.For<IExecutor<RandomImageFetchInput, RandomImageFetchOutput>>();
        interAgentMessenger.ChatSessionAsync(
                Arg.Any<openLuo.Modules.InterAgent.Core.Models.InterAgentChatSessionRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new openLuo.Modules.InterAgent.Core.Models.InterAgentChatSessionResult
            {
                Success = true,
                TargetCharacterId = "c2",
                TargetDisplayName = "艾莉娅",
                Transcript =
                [
                    new openLuo.Modules.InterAgent.Core.Models.InterAgentDialogueTurn
                    {
                        SpeakerCharacterId = "c1",
                        SpeakerDisplayName = "铃",
                        Content = "你好呀。"
                    },
                    new openLuo.Modules.InterAgent.Core.Models.InterAgentDialogueTurn
                    {
                        SpeakerCharacterId = "c2",
                        SpeakerDisplayName = "艾莉娅",
                        Content = "下午好。"
                    }
                ]
            });

        var sut = new UnifiedAgentCapabilityExecutor(commandBridge, roster, interAgentMessenger, randomImageFetchExecutor);

        var result = await sut.ExecuteAsync(
            new AgentCapabilityDescriptor
            {
                Name = "chat_with_character_session",
                Category = "inter-agent",
                ExecutorKind = "inter-agent"
            },
            [],
            new Dictionary<string, string>
            {
                ["target"] = "艾莉娅",
                ["opening"] = "你好呀。"
            },
            new AgentCapabilityContext
            {
                GameId = "g1",
                CharacterId = "c1"
            });

        Assert.True(result.Success);
        Assert.Contains("铃：你好呀。", result.Output);
        Assert.Contains("艾莉娅：下午好。", result.Output);
    }
}
