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

        var result = await bridge.HandleAsync("group", 123, 456, "你好", senderName: "群友阿明", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("reply: 你好", result.FinalText);
        Assert.Equal("qq-group-123", runtime.LastSessionId);
        Assert.Equal("群友阿明", runtime.LastSenderName);
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

    private static Milky.Net.Model.IncomingSegment Mention(string userId) =>
        new Milky.Net.Model.IncomingSegment<Milky.Net.Model.MentionIncomingSegmentData>(new Milky.Net.Model.MentionIncomingSegmentData(long.Parse(userId), "bot"));
    private static Milky.Net.Model.IncomingSegment Text(string value) =>
        new Milky.Net.Model.IncomingSegment<Milky.Net.Model.TextIncomingSegmentData>(new Milky.Net.Model.TextIncomingSegmentData(value));

    private sealed class StubRuntime(string expectedSessionId) : IAgentRuntime
    {
        public string? LastSessionId { get; private set; }
        public string? LastSenderName { get; private set; }
        public Task<AgentSession> OpenSessionAsync(SessionOpenRequest request, CancellationToken ct = default)
        {
            LastSessionId = request.SessionId;
            Assert.Equal(expectedSessionId, request.SessionId);
            return Task.FromResult(new AgentSession { SessionId = request.SessionId, SubjectId = request.SubjectId, AgentId = request.AgentId, ConversationId = request.ConversationId });
        }
        public Task<TurnResult> RunTurnAsync(TurnRequest request, CancellationToken ct = default)
        {
            LastSenderName = request.SenderName;
            return Task.FromResult(new TurnResult { Success = true, FinalText = $"reply: {request.Text}" });
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
