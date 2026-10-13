using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using Xunit;

namespace openLuo.AgentContext.Tests;

/// <summary>会话侧历史窗口与预算（D44）。</summary>
public sealed class ContextSessionBudgetTests
{
    private sealed class RecordingStore(List<ConversationTurn> turns) : IConversationStore
    {
        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(string sessionId, int limit, CancellationToken ct = default)
        {
            LastLimit = limit;
            return Task.FromResult<IReadOnlyList<ConversationTurn>>([.. turns.TakeLast(limit)]);
        }

        public Task AppendAsync(ConversationTurn turn, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>只负责给出系统块占用的装配器替身（其余字段会话不消费）。</summary>
    private sealed class StubAssembler(int systemTokens) : IContextAssembler
    {
        public Task<AgentDecisionContext> BuildAsync(ContextBuildRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentDecisionContext
            {
                SessionId = request.SessionId,
                TurnId = request.TurnId,
                Budget = new ContextBudgetSnapshot { MaxTokens = 0, SystemTokens = systemTokens }
            });
    }

    private static List<ConversationTurn> Turns(int count, int contentLength) =>
        [.. Enumerable.Range(1, count).Select(i => new ConversationTurn
        {
            SessionId = "s",
            TurnId = $"t{i}",
            Content = new string('x', contentLength)
        })];

    private static DefaultAgentContextSession Session(
        IContextAssembler assembler, IConversationStore store, ContextLimits limits) =>
        new("s", "subject", assembler, store, new DefaultMessageTagPipeline(),
            limits: new StaticContextLimitsProvider(limits));

    [Fact]
    public async Task Snapshot_TrimsOldestTurnsWhenBudgetIsExhausted()
    {
        // 上限 30，系统块占 10 → 历史只有 20；三条各 10 token 的对话 → 只有最新两条能进。
        var store = new RecordingStore(Turns(3, 40));
        var session = Session(new StubAssembler(10), store, new ContextLimits { MaxTokens = 30, ConversationRetainCount = 10 });

        var snapshot = await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = "s", SubjectId = "subject", TurnId = "turn"
        }, CancellationToken.None);

        Assert.Equal(["t2", "t3"], snapshot.Conversation.Select(t => t.TurnId));
        Assert.Equal(20, snapshot.Budget.ConversationTokens);
        Assert.Equal(1, snapshot.Budget.ConversationTurnsDropped);
        Assert.Equal(30, snapshot.Budget.MaxTokens);
    }

    [Fact]
    public async Task Snapshot_UsesConfiguredRetainCountAsFetchCap()
    {
        var store = new RecordingStore(Turns(5, 4));
        var session = Session(new StubAssembler(0), store, new ContextLimits { MaxTokens = 1000, ConversationRetainCount = 2 });

        var snapshot = await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = "s", SubjectId = "subject", TurnId = "turn"
        }, CancellationToken.None);

        Assert.Equal(2, store.LastLimit);
        Assert.Equal(["t4", "t5"], snapshot.Conversation.Select(t => t.TurnId));
    }

    [Fact]
    public async Task Snapshot_AlwaysKeepsNewestTurnEvenWhenBudgetIsZero()
    {
        // 系统块已吃满预算：历史仍保留最新一条——"忘了刚才说了什么"比"略超预算"更糟。
        var store = new RecordingStore(Turns(3, 40));
        var session = Session(new StubAssembler(30), store, new ContextLimits { MaxTokens = 30, ConversationRetainCount = 10 });

        var snapshot = await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = "s", SubjectId = "subject", TurnId = "turn"
        }, CancellationToken.None);

        Assert.Equal("t3", Assert.Single(snapshot.Conversation).TurnId);
        Assert.Equal(10, snapshot.Budget.ConversationTokens);
        Assert.Equal(2, snapshot.Budget.ConversationTurnsDropped);
    }

    [Fact]
    public async Task Snapshot_ReportsSystemBudgetFromAssembler()
    {
        var session = Session(new StubAssembler(123), new RecordingStore([]), ContextLimits.Default);

        var snapshot = await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = "s", SubjectId = "subject", TurnId = "turn"
        }, CancellationToken.None);

        Assert.Equal(123, snapshot.Budget.SystemTokens);
        Assert.Empty(snapshot.Conversation);
    }
}
