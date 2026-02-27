using openLuo.Core.Models;
using openLuo.Core.Interfaces;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.InterAgent.Application;
using System.Text;

namespace openLuo.InterAgent.Tests;

public sealed class InterAgentMessengerTests
{
    [Fact]
    public async Task AskAsync_ResolvesTarget_EnsuresPartyStarted_AndReturnsReply()
    {
        var dispatcher = Substitute.For<IAgentDispatcher>();
        var roster = Substitute.For<IAgentRoster>();
        var services = Substitute.For<IServiceProvider>();
        var runtimeHub = Substitute.For<IAgentRuntimeHub>();
        roster.ResolveAsync("g1", "艾莉娅", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c2", Name = "艾莉娅", ArchetypeId = "bg2" });
        services.GetService(typeof(IAgentRuntimeHub)).Returns(runtimeHub);
        runtimeHub.RequestAsync(
                "c2",
                AgentMessageType.AgentAsk,
                "c1",
                Arg.Is<string>(x =>
                    x.Contains("内部咨询：请用 1 到 3 句") &&
                    x.Contains("问题：你的喜好是什么？")),
                "g1",
                Arg.Any<string?>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(new AgentMessage(
                MessageId: "m1",
                GameId: "g1",
                From: "c2",
                To: "c1",
                Type: AgentMessageType.AgentReply,
                Payload: "我喜欢红茶。",
                CorrelationId: "agentask_1",
                TimestampUtc: DateTimeOffset.UtcNow,
                InterAgentOutcome: new InterAgentOutcome
                {
                    ReplyText = "我喜欢红茶。"
                }));

        var sut = new InterAgentMessenger(dispatcher, roster, services);

        var result = await sut.AskAsync(new openLuo.Modules.InterAgent.Core.Models.InterAgentAskRequest
        {
            GameId = "g1",
            FromCharacterId = "c1",
            TargetSelector = "艾莉娅",
            Question = "你的喜好是什么？"
        });

        Assert.True(result.Success);
        Assert.Equal("c2", result.TargetCharacterId);
        Assert.Equal("艾莉娅", result.TargetDisplayName);
        Assert.Equal("我喜欢红茶。", result.Reply);
        Assert.NotNull(result.Outcome);
        Assert.Equal("我喜欢红茶。", result.Outcome!.ReplyText);
        await runtimeHub.Received(1).EnsurePartyStartedAsync("g1", Arg.Any<CancellationToken>());
        await runtimeHub.Received(1).RequestAsync(
            "c2",
            AgentMessageType.AgentAsk,
            "c1",
            Arg.Any<string>(),
            "g1",
            Arg.Any<string?>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChatSessionAsync_AlternatesTurns_AndStopsWhenAgentEndsDialogue()
    {
        var dispatcher = Substitute.For<IAgentDispatcher>();
        var roster = Substitute.For<IAgentRoster>();
        var services = Substitute.For<IServiceProvider>();
        var runtimeHub = Substitute.For<IAgentRuntimeHub>();
        var streams = new TestStreams();
        roster.ListAsync("g1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new Character { Id = "c1", Name = "铃", ArchetypeId = "bg1" },
                new Character { Id = "c2", Name = "艾莉娅", ArchetypeId = "bg2" }
            ]);
        roster.ResolveAsync("g1", "艾莉娅", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c2", Name = "艾莉娅", ArchetypeId = "bg2" });
        services.GetService(typeof(IAgentRuntimeHub)).Returns(runtimeHub);
        runtimeHub.RequestAsync(
                Arg.Any<string>(),
                Arg.Any<AgentMessageType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<AgentExecutionContext?>(),
                Arg.Any<IReadOnlyList<AgentContextBlock>?>(),
                (IReadOnlyList<Block>?)null, (IReadOnlyDictionary<string, string>?)null,
                Arg.Any<CancellationToken>())
            .Returns(
                new AgentMessage(
                    MessageId: "m1",
                    GameId: "g1",
                    From: "c2",
                    To: "c1",
                    Type: AgentMessageType.AgentDialogueTurn,
                    Payload: "今天阳光很好。",
                    CorrelationId: "chat_1",
                    TimestampUtc: DateTimeOffset.UtcNow,
                    EndDialogue: false),
                new AgentMessage(
                    MessageId: "m2",
                    GameId: "g1",
                    From: "c1",
                    To: "c2",
                    Type: AgentMessageType.AgentDialogueTurn,
                    Payload: "嗯，窗台也暖暖的。",
                    CorrelationId: "chat_1",
                    TimestampUtc: DateTimeOffset.UtcNow,
                    EndDialogue: true));

        var sut = new InterAgentMessenger(dispatcher, roster, services, streams: streams);

        var result = await sut.ChatSessionAsync(new openLuo.Modules.InterAgent.Core.Models.InterAgentChatSessionRequest
        {
            GameId = "g1",
            FromCharacterId = "c1",
            TargetSelector = "艾莉娅",
            Opening = "你好呀，今天过得怎么样？"
        });

        Assert.True(result.Success);
        Assert.Equal(3, result.Transcript.Count);
        Assert.Equal("铃", result.Transcript[0].SpeakerDisplayName);
        Assert.Equal("你好呀，今天过得怎么样？", result.Transcript[0].Content);
        Assert.Equal("艾莉娅", result.Transcript[1].SpeakerDisplayName);
        Assert.Equal("今天阳光很好。", result.Transcript[1].Content);
        Assert.Equal("铃", result.Transcript[2].SpeakerDisplayName);
        Assert.Equal("嗯，窗台也暖暖的。", result.Transcript[2].Content);
        await runtimeHub.Received(1).EnsurePartyStartedAsync("g1", Arg.Any<CancellationToken>());
        var visible = Encoding.UTF8.GetString(streams.OutputBuffer.ToArray());
        Assert.Contains("铃：你好呀，今天过得怎么样？", visible);
        Assert.Contains("艾莉娅：今天阳光很好。", visible);
        await runtimeHub.Received(1).RequestAsync(
            "c2",
            AgentMessageType.AgentDialogueTurn,
            "c1",
            Arg.Is<string>(x => x.Contains("内部对话") && x.Contains("你好呀，今天过得怎么样？")),
            "g1",
            Arg.Any<string?>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<AgentExecutionContext?>(),
            Arg.Any<IReadOnlyList<AgentContextBlock>?>(),
            (IReadOnlyList<Block>?)null, (IReadOnlyDictionary<string, string>?)null,
            Arg.Any<CancellationToken>());
        await runtimeHub.Received(1).RequestAsync(
            "c1",
            AgentMessageType.AgentDialogueTurn,
            "c2",
            Arg.Is<string>(x => x.Contains("内部对话") && x.Contains("今天阳光很好。")),
            "g1",
            Arg.Any<string?>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<AgentExecutionContext?>(),
            Arg.Any<IReadOnlyList<AgentContextBlock>?>(),
            (IReadOnlyList<Block>?)null, (IReadOnlyDictionary<string, string>?)null,
            Arg.Any<CancellationToken>());
    }

    private sealed class TestStreams : IGameStreams
    {
        private readonly MemoryStream _input = new();
        private readonly MemoryStream _output = new();
        private readonly MemoryStream _error = new();

        public Stream Input => _input;
        public Stream Output => _output;
        public Stream Error => _error;

        public MemoryStream OutputBuffer => _output;
    }
}
