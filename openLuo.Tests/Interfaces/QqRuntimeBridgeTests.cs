using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Interfaces.QQbot;

namespace openLuo.Interfaces.Tests;

public sealed class QqRuntimeBridgeTests
{
    [Fact]
    public async Task HandleAsync_RoutesTextThroughRuntimeTurn()
    {
        var runtime = new StubRuntime("qq-group-123");
        var bridge = new QqRuntimeBridge(runtime, new QqBotConfig { DefaultSubjectId = "builtin-rin", DefaultAgentId = "companion" });

        var result = await bridge.HandleAsync("group", 123, 456, "你好", senderName: "群友阿明", ct: CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("reply: 你好", result.FinalText);
        Assert.Equal("qq-group-123", runtime.LastSessionId);
        Assert.Equal("群友阿明", runtime.LastSenderName);
        Assert.Equal(0, runtime.ObservedCount); // 完整 turn 不触发感知通道
    }

    [Fact]
    public async Task ObserveAsync_WritesMessageWithoutTriggeringTurn()
    {
        var runtime = new StubRuntime("qq-group-123");
        var bridge = new QqRuntimeBridge(runtime, new QqBotConfig { DefaultSubjectId = "builtin-rin", DefaultAgentId = "companion" });

        await bridge.ObserveAsync("group", 123, "闲聊消息", senderName: "群友阿明", ct: CancellationToken.None);

        Assert.Equal("qq-group-123", runtime.LastSessionId);
        Assert.Equal("闲聊消息", runtime.LastObservedText);
        Assert.Equal("群友阿明", runtime.LastObservedSender);
        Assert.Equal(0, runtime.TurnCount); // 感知通道绝不触发 LLM 回合
    }

    [Fact]
    public void Render_IncludesFinalTextAndTextOutputs()
    {
        var result = new TurnResult
        {
            Success = true,
            FinalText = "final",
            Outputs = [new OutputItem { Kind = ReplyItemKind.Text, Payload = "extra" }]
        };
        var parts = QqRuntimeBridge.Render(result);
        Assert.Contains(parts, p => p.Kind == "text" && p.Value == "extra");
        Assert.Contains(parts, p => p.Kind == "text" && p.Value == "final");
    }

    [Fact]
    public void ExtractText_JoinsSegmentsAndSkipsBotMention()
    {
        var segments = new[] { Mention("123"), Text("hello"), Text("world") };
        Assert.Equal("hello world", QqBotApplication.ExtractText(segments, 123));
        Assert.Equal("@bot hello world", QqBotApplication.ExtractText(segments, 456));
    }

    [Fact]
    public void CollectImageUrls_ExtractsTempUrls()
    {
        var segments = new Milky.Net.Model.IncomingSegment[]
        {
            Text("hello"),
            new Milky.Net.Model.IncomingSegment<Milky.Net.Model.ImageIncomingSegmentData>(
                new Milky.Net.Model.ImageIncomingSegmentData("r1", "https://img.example/a.png", 100, 100, "img", Milky.Net.Model.SubType.Normal)),
            Text("world")
        };
        Assert.Equal(["https://img.example/a.png"], QqBotApplication.CollectImageUrls(segments));
    }

    [Fact]
    public void CollectImageUrls_SkipsSegmentsWithoutUrl()
    {
        var segments = new Milky.Net.Model.IncomingSegment[]
        {
            new Milky.Net.Model.IncomingSegment<Milky.Net.Model.ImageIncomingSegmentData>(
                new Milky.Net.Model.ImageIncomingSegmentData("r2", "", 50, 50, "no-url", Milky.Net.Model.SubType.Normal))
        };
        Assert.Empty(QqBotApplication.CollectImageUrls(segments));
    }

    [Fact]
    public async Task ObserveAsync_PropagatesImageBlocks()
    {
        var runtime = new StubRuntime("qq-group-123");
        var bridge = new QqRuntimeBridge(runtime, new QqBotConfig { DefaultSubjectId = "builtin-rin", DefaultAgentId = "companion" });
        var block = new openLuo.Core.Models.ImageBlock
        {
            Kind = openLuo.Core.Models.BlockKind.Image, AssetId = "a1", MimeType = "image/png"
        };

        await bridge.ObserveAsync("group", 123, "看图", senderName: "阿明", blocks: [block], CancellationToken.None);

        Assert.Same(block, Assert.Single(runtime.LastObservedBlocks!));
    }

    [Fact]
    public async Task HandleAsync_PropagatesImageBlocksToTurn()
    {
        var runtime = new StubRuntime("qq-group-123");
        var bridge = new QqRuntimeBridge(runtime, new QqBotConfig { DefaultSubjectId = "builtin-rin", DefaultAgentId = "companion" });
        var block = new openLuo.Core.Models.ImageBlock
        {
            Kind = openLuo.Core.Models.BlockKind.Image, AssetId = "a1", MimeType = "image/png"
        };

        await bridge.HandleAsync("group", 123, 456, "看图", senderName: "阿明", blocks: [block], CancellationToken.None);

        Assert.Same(block, Assert.Single(runtime.LastTurnBlocks!));
    }

    private static Milky.Net.Model.IncomingSegment Mention(string userId) =>
        new Milky.Net.Model.IncomingSegment<Milky.Net.Model.MentionIncomingSegmentData>(new Milky.Net.Model.MentionIncomingSegmentData(long.Parse(userId), "bot"));
    private static Milky.Net.Model.IncomingSegment Text(string value) =>
        new Milky.Net.Model.IncomingSegment<Milky.Net.Model.TextIncomingSegmentData>(new Milky.Net.Model.TextIncomingSegmentData(value));

    private sealed class StubRuntime(string expectedSessionId) : IAgentRuntime
    {
        public string? LastSessionId { get; private set; }
        public string? LastSenderName { get; private set; }
        public string? LastObservedText { get; private set; }
        public string? LastObservedSender { get; private set; }
        public IReadOnlyList<object>? LastObservedBlocks { get; private set; }
        public IReadOnlyList<object>? LastTurnBlocks { get; private set; }
        public int TurnCount { get; private set; }
        public int ObservedCount { get; private set; }
        public Task<AgentSession> OpenSessionAsync(SessionOpenRequest request, CancellationToken ct = default)
        {
            LastSessionId = request.SessionId;
            Assert.Equal(expectedSessionId, request.SessionId);
            return Task.FromResult(new AgentSession { SessionId = request.SessionId, SubjectId = request.SubjectId, AgentId = request.AgentId, ConversationId = request.ConversationId });
        }
        public Task<TurnResult> RunTurnAsync(TurnRequest request, CancellationToken ct = default)
        {
            TurnCount++;
            LastSenderName = request.SenderName;
            LastTurnBlocks = request.Blocks;
            return Task.FromResult(new TurnResult { Success = true, FinalText = $"reply: {request.Text}" });
        }
        public Task AppendMessageAsync(string sessionId, string? senderName, string text, IReadOnlyList<object>? blocks = null, CancellationToken ct = default)
        {
            ObservedCount++;
            LastSessionId = sessionId;
            LastObservedSender = senderName;
            LastObservedText = text;
            LastObservedBlocks = blocks;
            return Task.CompletedTask;
        }
        public Task<string?> GetContextSummaryAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult<string?>($"summary: {sessionId}");
        public async IAsyncEnumerable<TurnEvent> StreamTurnAsync(TurnRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new TurnEvent { TurnId = request.TurnId, Kind = "final" };
            await Task.CompletedTask;
        }
    }
}
