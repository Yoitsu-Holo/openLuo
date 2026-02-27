using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using NSubstitute;
using Xunit;

namespace openLuo.Capabilities.Tests;

public class DefaultCapabilityDecisionLoopTests
{
    private static CapabilityDescriptor MakeDescriptor(string canonicalId, SideEffectClass sideEffect = SideEffectClass.ReadOnly, CompletionPolicy completion = CompletionPolicy.Continue) =>
        new()
        {
            CanonicalId = canonicalId,
            ModelToolName = canonicalId.Replace(':', '_'),
            Kind = CapabilityKind.Builtin,
            ProviderId = "test",
            SideEffect = sideEffect,
            Completion = completion
        };

    private sealed class FakeContextUpdater : IContextUpdater
    {
        public List<CapabilityResult> LastResults { get; private set; } = [];
        public Task<CapabilityDecisionContext> ApplyToolResultsAsync(
            string sessionId, string turnId, IReadOnlyList<CapabilityCall> calls, IReadOnlyList<CapabilityResult> results, CancellationToken ct = default)
        {
            LastResults = results.ToList();
            return Task.FromResult(new CapabilityDecisionContext { SessionId = sessionId, TurnId = turnId });
        }
    }

    private static CapabilityCatalogSnapshot Snapshot(params CapabilityDescriptor[] descriptors) =>
        new()
        {
            ByCanonicalId = descriptors.ToDictionary(d => d.CanonicalId, StringComparer.OrdinalIgnoreCase),
            ModelNameToCanonicalId = descriptors.ToDictionary(d => d.ModelToolName, d => d.CanonicalId, StringComparer.OrdinalIgnoreCase),
            CanonicalIdToModelName = descriptors.ToDictionary(d => d.CanonicalId, d => d.ModelToolName, StringComparer.OrdinalIgnoreCase)
        };

    [Fact]
    public async Task Run_ModelReturnsFinalText_EndsWithFinalReply()
    {
        var model = Substitute.For<ICapabilityDecisionModel>();
        model.DecideAsync(Arg.Any<CapabilityDecisionContext>(), Arg.Any<CancellationToken>())
            .Returns(new CapabilityDecision { FinalText = "你好，今天过得怎么样？" });

        var invoker = Substitute.For<ICapabilityInvoker>();
        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());
        var loop = new DefaultCapabilityDecisionLoop(model, dispatcher, new FakeContextUpdater(), new SystemClock());

        var result = await loop.RunAsync(new DecisionLoopRequest
        {
            SessionId = "s1",
            TurnId = "t1",
            Context = new CapabilityDecisionContext(),
            Catalog = Snapshot(),
            Budgets = DecisionBudgets.Default
        });

        Assert.True(result.Success);
        Assert.Equal(TerminationReason.FinalReply, result.TerminationReason);
        Assert.Equal("你好，今天过得怎么样？", result.FinalText);
        Assert.Equal(1, result.DecisionsUsed);
    }

    [Fact]
    public async Task Run_ToolCallThenFinalText_ExecutesToolAndContinues()
    {
        var model = Substitute.For<ICapabilityDecisionModel>();
        model.DecideAsync(Arg.Any<CapabilityDecisionContext>(), Arg.Any<CancellationToken>())
            .Returns(
                new CapabilityDecision
                {
                    Calls =
                    [
                        new CapabilityCall { InvocationId = "inv-1", IdempotencyKey = "k1", CanonicalId = "test:lookup", ParentDecisionId = "d1" }
                    ]
                },
                new CapabilityDecision { FinalText = "查到了" });

        var invoker = Substitute.For<ICapabilityInvoker>();
        invoker.InvokeAsync(Arg.Any<CapabilityCall>(), Arg.Any<CapabilityExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new CapabilityResult { InvocationId = "inv-1", Success = true, Status = CapabilityStatus.Ok, Text = "result-1" });

        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());
        var updater = new FakeContextUpdater();
        var loop = new DefaultCapabilityDecisionLoop(model, dispatcher, updater, new SystemClock());

        var result = await loop.RunAsync(new DecisionLoopRequest
        {
            SessionId = "s1",
            TurnId = "t1",
            Context = new CapabilityDecisionContext(),
            Catalog = Snapshot(MakeDescriptor("test:lookup")),
            Budgets = DecisionBudgets.Default
        });

        Assert.True(result.Success);
        Assert.Equal("查到了", result.FinalText);
        Assert.Equal(2, result.DecisionsUsed);
        Assert.Single(updater.LastResults);
        await invoker.Received(1).InvokeAsync(Arg.Any<CapabilityCall>(), Arg.Any<CapabilityExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_EmptyReplies_EndsWithEmptyReply()
    {
        var model = Substitute.For<ICapabilityDecisionModel>();
        model.DecideAsync(Arg.Any<CapabilityDecisionContext>(), Arg.Any<CancellationToken>())
            .Returns(new CapabilityDecision());   // 一直空回复

        var dispatcher = new DefaultCapabilityDispatcher(
            Substitute.For<ICapabilityInvoker>(), new DefaultCapabilityPolicy(), new InMemoryStateTransaction());
        var loop = new DefaultCapabilityDecisionLoop(model, dispatcher, new FakeContextUpdater(), new SystemClock());

        var result = await loop.RunAsync(new DecisionLoopRequest
        {
            SessionId = "s1",
            TurnId = "t1",
            Context = new CapabilityDecisionContext(),
            Catalog = Snapshot(),
            Budgets = new DecisionBudgets { MaxDecisions = 5 }
        });

        Assert.False(result.Success);
        Assert.Equal(TerminationReason.EmptyReply, result.TerminationReason);
    }

    [Fact]
    public async Task Run_TerminalCapability_EndsAfterExecution()
    {
        var model = Substitute.For<ICapabilityDecisionModel>();
        model.DecideAsync(Arg.Any<CapabilityDecisionContext>(), Arg.Any<CancellationToken>())
            .Returns(
                new CapabilityDecision
                {
                    Calls =
                    [
                        new CapabilityCall { InvocationId = "inv-1", IdempotencyKey = "k1", CanonicalId = "test:terminal", ParentDecisionId = "d1" }
                    ]
                },
                new CapabilityDecision { FinalText = "不应到达" });

        var invoker = Substitute.For<ICapabilityInvoker>();
        invoker.InvokeAsync(Arg.Any<CapabilityCall>(), Arg.Any<CapabilityExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new CapabilityResult { InvocationId = "inv-1", Success = true, Status = CapabilityStatus.Ok, Text = "done" });

        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());
        var loop = new DefaultCapabilityDecisionLoop(model, dispatcher, new FakeContextUpdater(), new SystemClock());

        var result = await loop.RunAsync(new DecisionLoopRequest
        {
            SessionId = "s1",
            TurnId = "t1",
            Context = new CapabilityDecisionContext(),
            Catalog = Snapshot(MakeDescriptor("test:terminal", completion: CompletionPolicy.Terminal)),
            Budgets = DecisionBudgets.Default
        });

        Assert.True(result.Success);
        Assert.Equal(TerminationReason.TerminalCapability, result.TerminationReason);
        Assert.Equal(1, result.DecisionsUsed);
    }

    [Fact]
    public async Task Run_RejectedBatch_FeedsBackReasonAndContinues()
    {
        // 回归：batch 被策略拒绝（如 unknown capability）时，拒绝原因必须回填为
        // tool 结果——否则 context 不变、模型空转直至 MaxDecisions。
        var model = Substitute.For<ICapabilityDecisionModel>();
        model.DecideAsync(Arg.Any<CapabilityDecisionContext>(), Arg.Any<CancellationToken>())
            .Returns(
                new CapabilityDecision
                {
                    Calls =
                    [
                        new CapabilityCall { InvocationId = "inv-1", CanonicalId = "test:unknown", ModelCallId = "call_01", ModelToolName = "cap_test_unknown_deadbeef" }
                    ]
                },
                new CapabilityDecision { FinalText = "已纠正" });

        var invoker = Substitute.For<ICapabilityInvoker>();
        var updater = new FakeContextUpdater();
        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());
        var loop = new DefaultCapabilityDecisionLoop(model, dispatcher, updater, new SystemClock());

        var result = await loop.RunAsync(new DecisionLoopRequest
        {
            SessionId = "s1",
            TurnId = "t1",
            Context = new CapabilityDecisionContext(),
            Catalog = Snapshot(MakeDescriptor("test:known")),
            Budgets = DecisionBudgets.Default
        });

        // 拒绝原因回填（第二次决策前 updater 收到 rejected 结果）
        Assert.NotEmpty(updater.LastResults);
        Assert.False(updater.LastResults[0].Success);
        Assert.Equal(CapabilityStatus.Rejected, updater.LastResults[0].Status);
        Assert.Contains("unknown capability", updater.LastResults[0].Error);
        // 模型看到错误后给出最终回复
        Assert.True(result.Success);
        Assert.Equal("已纠正", result.FinalText);
        Assert.Equal(TerminationReason.FinalReply, result.TerminationReason);
    }
}
