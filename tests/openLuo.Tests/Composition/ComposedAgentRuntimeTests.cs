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

    private static ComposedAgentRuntime CreateRuntime(IConversationStore store, SessionStore? sessions = null) => new(
        Substitute.For<ICapabilityCatalog>(),
        Substitute.For<ICapabilityDecisionLoop>(),
        Substitute.For<IContextAssembler>(),
        store,
        Substitute.For<IMessageTagPipeline>(),
        Substitute.For<IOutputQueue>(),
        sessions ?? new SessionStore());

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

    // ── 会话寻址 / 多端共享同一段对话（PC 客户端 + 手机 QQ 落到同一上下文） ──────────────

    [Fact]
    public async Task OpenSessionAsync_相同conversation复用同一会话()
    {
        var runtime = CreateRuntime(new MemoryConversationStore());

        var first = await runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = "sess_A", SubjectId = "builtin-rin", AgentId = "companion", ConversationId = "main",
        }, CancellationToken.None);
        var second = await runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = "sess_B", SubjectId = "builtin-rin", AgentId = "companion", ConversationId = "main",
        }, CancellationToken.None);

        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal("main", second.ConversationId);
        Assert.Single(await runtime.ListSessionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OpenSessionAsync_不同conversation或subject保持独立()
    {
        var runtime = CreateRuntime(new MemoryConversationStore());
        Task<AgentSession> Open(string subject, string conversation) => runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = $"sess_{Guid.NewGuid():N}", SubjectId = subject, AgentId = "companion", ConversationId = conversation,
        }, CancellationToken.None);

        var main = await Open("builtin-rin", "main");
        var other = await Open("builtin-rin", "qq-group-2");   // 同 subject，不同对话 → 独立
        var otherSubject = await Open("someone-else", "main"); // 同对话，不同 subject（记忆/状态按 subject 分区的支点）

        Assert.Equal(3, (await runtime.ListSessionsAsync(CancellationToken.None)).Count);
        Assert.NotEqual(main.SessionId, other.SessionId);
        Assert.NotEqual(main.SessionId, otherSubject.SessionId);
    }

    [Fact]
    public async Task OpenSessionAsync_未给conversationId时行为不变_每次新建()
    {
        var runtime = CreateRuntime(new MemoryConversationStore());

        var a = await runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = "sess_A", SubjectId = "builtin-rin", AgentId = "companion",
        }, CancellationToken.None);
        var b = await runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = "sess_B", SubjectId = "builtin-rin", AgentId = "companion",
        }, CancellationToken.None);

        Assert.NotEqual(a.SessionId, b.SessionId);
        Assert.Equal("sess_A", a.ConversationId);   // 缺省 conversationId = 本次 sessionId（保持原语义）
    }

    [Fact]
    public async Task OpenSessionAsync_重启后仍按conversation复用_hubdb重建反向索引()
    {
        var dir = Path.Combine(Path.GetTempPath(), "openluo-reuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dbPath = Path.Combine(dir, "hub.db");
            var first = CreateRuntime(new MemoryConversationStore(), new SessionStore(new openLuo.Infrastructure.Persistence.HubStore(dbPath)));
            var opened = await first.OpenSessionAsync(new SessionOpenRequest
            {
                SessionId = "sess_A", SubjectId = "builtin-rin", AgentId = "companion", ConversationId = "main",
            }, CancellationToken.None);

            // 模拟重启：全新的 store 仅凭 hub.db 重建反向索引
            var afterRestart = CreateRuntime(new MemoryConversationStore(), new SessionStore(new openLuo.Infrastructure.Persistence.HubStore(dbPath)));
            var again = await afterRestart.OpenSessionAsync(new SessionOpenRequest
            {
                SessionId = "sess_B", SubjectId = "builtin-rin", AgentId = "companion", ConversationId = "main",
            }, CancellationToken.None);

            Assert.Equal(opened.SessionId, again.SessionId);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
