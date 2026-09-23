using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using openLuo.Composition;
using openLuo.AgentContext.Infrastructure;
using Xunit;

namespace openLuo.E2E.Tests;

public sealed class ComposedRuntimeE2ETests
{
    [Fact]
    public async Task Runtime_OpenSessionAndRunTurn_ReturnsFinalText()
    {
        var catalog = new DefaultCapabilityCatalog([]);
        catalog.LoadBase();
        var loop = new DefaultCapabilityDecisionLoop(
            new FinalTextModel(), new DefaultCapabilityDispatcher(new NoOpInvoker(), new DefaultCapabilityPolicy(), new InMemoryStateTransaction()),
            new NoOpContextUpdater(), new SystemClock());
        var runtime = new ComposedAgentRuntime(
            catalog, loop, new DefaultContextAssembler([]), new MemoryConversationStore(),
            new DefaultMessageTagPipeline(), new InMemoryOutputQueue(), new SessionStore());
        var session = await runtime.OpenSessionAsync(new SessionOpenRequest { SessionId = "s", SubjectId = "player", AgentId = "demo" });

        var result = await runtime.RunTurnAsync(new TurnRequest { SessionId = session.SessionId, TurnId = "t", Text = "hello" });

        Assert.True(result.Success);
        Assert.Equal("final: hello", result.FinalText);
        Assert.Equal(TerminationReason.FinalReply, result.TerminationReason);
    }

    [Fact]
    public async Task ContextSummary_AfterTurn_ContainsSnapshotAndHistory()
    {
        var catalog = new DefaultCapabilityCatalog([]);
        catalog.LoadBase();
        var loop = new DefaultCapabilityDecisionLoop(
            new FinalTextModel(), new DefaultCapabilityDispatcher(new NoOpInvoker(), new DefaultCapabilityPolicy(), new InMemoryStateTransaction()),
            new NoOpContextUpdater(), new SystemClock());
        var store = new MemoryConversationStore();
        var runtime = new ComposedAgentRuntime(
            catalog, loop, new DefaultContextAssembler([]), store,
            new DefaultMessageTagPipeline(), new InMemoryOutputQueue(), new SessionStore());
        await runtime.OpenSessionAsync(new SessionOpenRequest { SessionId = "s", SubjectId = "player", AgentId = "demo" });

        await runtime.RunTurnAsync(new TurnRequest { SessionId = "s", TurnId = "t1", Text = "hello", SenderName = "阿明" });

        var summary = await runtime.GetContextSummaryAsync("s");
        Assert.NotNull(summary);
        Assert.Contains("session: s", summary);
        Assert.Contains("inbound(阿明): hello", summary);   // 发送者标识进历史
        Assert.Null(await runtime.GetContextSummaryAsync("unknown-session"));
    }

    [Fact]
    public async Task PlatformMeta_InjectsDynamicPlatformBlock()
    {
        var catalog = new DefaultCapabilityCatalog([]);
        catalog.LoadBase();
        var loop = new DefaultCapabilityDecisionLoop(
            new FinalTextModel(), new DefaultCapabilityDispatcher(new NoOpInvoker(), new DefaultCapabilityPolicy(), new InMemoryStateTransaction()),
            new NoOpContextUpdater(), new SystemClock());
        var runtime = new ComposedAgentRuntime(
            catalog, loop, new DefaultContextAssembler([]), new MemoryConversationStore(),
            new DefaultMessageTagPipeline(), new InMemoryOutputQueue(), new SessionStore());
        await runtime.OpenSessionAsync(new SessionOpenRequest { SessionId = "s", SubjectId = "player", AgentId = "demo" });

        var withMeta = new ComposedAgentRuntime(
            catalog, loop, new DefaultContextAssembler([new PlatformContextContributor()]), new MemoryConversationStore(),
            new DefaultMessageTagPipeline(), new InMemoryOutputQueue(), new SessionStore());
        await withMeta.OpenSessionAsync(new SessionOpenRequest { SessionId = "s2", SubjectId = "player", AgentId = "demo" });

        await withMeta.RunTurnAsync(new TurnRequest
        {
            SessionId = "s2", TurnId = "t2", Text = "在吗",
            Meta = new Dictionary<string, object?> { ["scene"] = "group", ["senderName"] = "阿明", ["channelId"] = "123" }
        });

        var summary = await withMeta.GetContextSummaryAsync("s2");
        Assert.Contains("[Platform] scene: group; current sender: 阿明; channel: 123", summary);
    }
    [Fact]
    public async Task DecisionLoop_StopsAtDecisionBudget()
    {
        var loop = new DefaultCapabilityDecisionLoop(new EndlessCallModel(), new DefaultCapabilityDispatcher(new NoOpInvoker(), new DefaultCapabilityPolicy(), new InMemoryStateTransaction()), new NoOpContextUpdater(), new SystemClock());
        var result = await loop.RunAsync(new DecisionLoopRequest
        {
            Context = new CapabilityDecisionContext { Budgets = new DecisionBudgets { MaxDecisions = 1 } },
            Catalog = new CapabilityCatalogSnapshot { ByCanonicalId = new Dictionary<string, CapabilityDescriptor> { ["demo:noop"] = new CapabilityDescriptor { CanonicalId = "demo:noop" } } },
            Budgets = new DecisionBudgets { MaxDecisions = 1 }
        });
        Assert.Equal(TerminationReason.MaxDecisionsReached, result.TerminationReason);
    }

    private sealed class EndlessCallModel : ICapabilityDecisionModel
    {
        public Task<CapabilityDecision> DecideAsync(CapabilityDecisionContext context, CancellationToken ct = default) =>
            Task.FromResult(new CapabilityDecision { Calls = [new CapabilityCall { InvocationId = Guid.NewGuid().ToString("N"), CanonicalId = "demo:noop" }] });
    }

    private sealed class FinalTextModel : ICapabilityDecisionModel
    {
        public Task<CapabilityDecision> DecideAsync(CapabilityDecisionContext context, CancellationToken ct = default) =>
            Task.FromResult(new CapabilityDecision
            {
                Messages = [new FlowItem { Mode = FlowMode.Respond, Kind = ReplyItemKind.Text, Payload = $"final: {context.UserInput}" }]
            });
    }
    private sealed class NoOpInvoker : ICapabilityInvoker
    {
        public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default) =>
            Task.FromResult(new CapabilityResult { InvocationId = call.InvocationId, Success = true, Status = CapabilityStatus.Ok });
    }
    private sealed class NoOpContextUpdater : IContextUpdater
    {
        public Task<CapabilityDecisionContext> ApplyToolResultsAsync(string sessionId, string turnId, IReadOnlyList<CapabilityCall> calls, IReadOnlyList<CapabilityResult> results, CancellationToken ct = default) =>
            Task.FromResult(new CapabilityDecisionContext { SessionId = sessionId, TurnId = turnId });
    }
    private sealed class MemoryConversationStore : IConversationStore
    {
        private readonly List<ConversationTurn> _turns = [];
        public Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(string sessionId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConversationTurn>>(_turns);
        public Task AppendAsync(ConversationTurn turn, CancellationToken ct = default) { _turns.Add(turn); return Task.CompletedTask; }
    }
}
