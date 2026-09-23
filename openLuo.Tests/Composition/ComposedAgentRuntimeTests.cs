using NSubstitute;
using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using openLuo.Composition;

namespace openLuo.Tests.Composition;

public sealed class ComposedAgentRuntimeTests
{
    private sealed class MemoryConversationStore : IConversationStore
    {
        public List<ConversationTurn> Turns { get; } = [];

        public Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(string sessionId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConversationTurn>>(Turns.Where(t => t.SessionId == sessionId).TakeLast(limit).ToList());

        public Task AppendAsync(ConversationTurn turn, CancellationToken ct = default)
        {
            Turns.Add(turn);
            return Task.CompletedTask;
        }
    }

    private static ComposedAgentRuntime CreateRuntime(IConversationStore store) => new(
        Substitute.For<ICapabilityCatalog>(),
        Substitute.For<ICapabilityDecisionLoop>(),
        Substitute.For<IContextAssembler>(),
        store,
        Substitute.For<IMessageTagPipeline>(),
        Substitute.For<IOutputQueue>(),
        new SessionStore());

    [Fact]
    public async Task AppendMessageAsync_WritesInboundTurnWithoutLlm()
    {
        var store = new MemoryConversationStore();
        var runtime = CreateRuntime(store);
        await runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = "qq-group-1", SubjectId = "builtin-rin", AgentId = "companion", ConversationId = "qq-group-1"
        }, CancellationToken.None);

        await runtime.AppendMessageAsync("qq-group-1", "阿明", "群聊闲聊", ct: CancellationToken.None);

        var turn = Assert.Single(store.Turns);
        Assert.Equal("qq-group-1", turn.SessionId);
        Assert.EndsWith(":observe", turn.TurnId);
        Assert.Equal("inbound", turn.SpeakerRole);
        Assert.Equal("阿明", turn.SpeakerId);
        Assert.Equal("阿明", turn.SpeakerName);
        Assert.Equal("群聊闲聊", turn.Content);
    }

    [Fact]
    public async Task AppendMessageAsync_FallsBackToSubjectAsSpeaker()
    {
        var store = new MemoryConversationStore();
        var runtime = CreateRuntime(store);
        await runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = "qq-group-1", SubjectId = "builtin-rin", AgentId = "companion", ConversationId = "qq-group-1"
        }, CancellationToken.None);

        await runtime.AppendMessageAsync("qq-group-1", null, "无昵称消息", ct: CancellationToken.None);

        var turn = Assert.Single(store.Turns);
        Assert.Equal("builtin-rin", turn.SpeakerId);
    }

    [Fact]
    public async Task AppendMessageAsync_RequiresOpenSession()
    {
        var runtime = CreateRuntime(new MemoryConversationStore());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.AppendMessageAsync("qq-group-1", null, "hi", ct: CancellationToken.None));
    }

    [Fact]
    public async Task AppendMessageAsync_PersistsImageBlocks()
    {
        var store = new MemoryConversationStore();
        var runtime = CreateRuntime(store);
        await runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = "qq-group-1", SubjectId = "builtin-rin", AgentId = "companion", ConversationId = "qq-group-1"
        }, CancellationToken.None);
        var block = new openLuo.Core.Models.ImageBlock
        {
            Kind = openLuo.Core.Models.BlockKind.Image, AssetId = "a1", MimeType = "image/png"
        };

        await runtime.AppendMessageAsync("qq-group-1", "阿明", "看图", [block], CancellationToken.None);

        var turn = Assert.Single(store.Turns);
        Assert.Same(block, Assert.Single(turn.Blocks!));
    }
}
