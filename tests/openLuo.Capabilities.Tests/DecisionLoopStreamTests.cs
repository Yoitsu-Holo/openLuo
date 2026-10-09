using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using NSubstitute;
using Xunit;

namespace openLuo.Capabilities.Tests;

/// <summary>决策循环真流式：事件按 Decision → ToolCall/ToolResult/Output → Final 顺序产出。</summary>
public class DecisionLoopStreamTests
{
    private static CapabilityDescriptor Descriptor(string canonicalId) => new()
    {
        CanonicalId = canonicalId,
        ModelToolName = canonicalId.Replace(':', '_'),
        Kind = CapabilityKind.Builtin,
        ProviderId = "test"
    };

    private static CapabilityCatalogSnapshot Snapshot(params CapabilityDescriptor[] descriptors) => new()
    {
        ByCanonicalId = descriptors.ToDictionary(d => d.CanonicalId, StringComparer.OrdinalIgnoreCase),
        ModelNameToCanonicalId = descriptors.ToDictionary(d => d.ModelToolName, d => d.CanonicalId, StringComparer.OrdinalIgnoreCase),
        CanonicalIdToModelName = descriptors.ToDictionary(d => d.CanonicalId, d => d.ModelToolName, StringComparer.OrdinalIgnoreCase)
    };

    private sealed class FakeContextUpdater : IContextUpdater
    {
        public Task<CapabilityDecisionContext> ApplyToolResultsAsync(
            string sessionId, string turnId, IReadOnlyList<CapabilityCall> calls,
            IReadOnlyList<CapabilityResult> results, CancellationToken ct = default)
            => Task.FromResult(new CapabilityDecisionContext { SessionId = sessionId, TurnId = turnId });
    }

    [Fact]
    public async Task RunStream_EmitsOrderedEvents()
    {
        var model = Substitute.For<ICapabilityDecisionModel>();
        model.DecideAsync(Arg.Any<CapabilityDecisionContext>(), Arg.Any<CancellationToken>())
            .Returns(
                new CapabilityDecision
                {
                    Calls = [new CapabilityCall { InvocationId = "inv-1", IdempotencyKey = "k1", CanonicalId = "test:lookup", ParentDecisionId = "d1" }]
                },
                new CapabilityDecision { Messages = [new FlowItem { Mode = FlowMode.Respond, Kind = ReplyItemKind.Text, Payload = "好了" }] });

        var invoker = Substitute.For<ICapabilityInvoker>();
        invoker.InvokeAsync(Arg.Any<CapabilityCall>(), Arg.Any<CapabilityExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new CapabilityResult { InvocationId = "inv-1", Success = true, Status = CapabilityStatus.Ok, Text = "r" });

        var loop = new DefaultCapabilityDecisionLoop(
            model,
            new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction()),
            new FakeContextUpdater(),
            new SystemClock());

        var kinds = new List<DecisionEventKind>();
        DecisionLoopResult? final = null;
        await foreach (var evt in loop.RunStreamAsync(new DecisionLoopRequest
        {
            SessionId = "s1", TurnId = "t1",
            Context = new CapabilityDecisionContext(),
            Catalog = Snapshot(Descriptor("test:lookup")),
            Budgets = DecisionBudgets.Default,
        }))
        {
            kinds.Add(evt.Kind);
            if (evt.Kind == DecisionEventKind.Final)
                final = (DecisionLoopResult)evt.Payload!;
        }

        Assert.NotNull(final);
        Assert.Equal("好了", final!.FinalText);
        Assert.Equal(DecisionEventKind.Decision, kinds[0]);
        Assert.Equal(DecisionEventKind.Final, kinds[^1]);
        Assert.Contains(DecisionEventKind.ToolCall, kinds);
        Assert.Contains(DecisionEventKind.ToolResult, kinds);
        Assert.True(kinds.IndexOf(DecisionEventKind.ToolCall) < kinds.IndexOf(DecisionEventKind.ToolResult));
    }

    [Fact]
    public async Task RunStream_EmitsOutput_ForInqueueMessage_AndEnqueuesToQueue()
    {
        var model = Substitute.For<ICapabilityDecisionModel>();
        model.DecideAsync(Arg.Any<CapabilityDecisionContext>(), Arg.Any<CancellationToken>())
            .Returns(
                new CapabilityDecision
                {
                    Messages = [new FlowItem { Mode = FlowMode.Inqueue, Kind = ReplyItemKind.Text, Payload = "稍等" }],
                    Calls = [new CapabilityCall { InvocationId = "inv-1", IdempotencyKey = "k1", CanonicalId = "test:lookup", ParentDecisionId = "d1" }]
                },
                new CapabilityDecision { Messages = [new FlowItem { Mode = FlowMode.Respond, Kind = ReplyItemKind.Text, Payload = "完成" }] });

        var invoker = Substitute.For<ICapabilityInvoker>();
        invoker.InvokeAsync(Arg.Any<CapabilityCall>(), Arg.Any<CapabilityExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new CapabilityResult { InvocationId = "inv-1", Success = true, Status = CapabilityStatus.Ok, Text = "r" });

        var queue = new InMemoryOutputQueue();
        var loop = new DefaultCapabilityDecisionLoop(
            model,
            new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction()),
            new FakeContextUpdater(),
            new SystemClock());

        var outputs = new List<OutputItem>();
        await foreach (var evt in loop.RunStreamAsync(new DecisionLoopRequest
        {
            SessionId = "s1", TurnId = "t1",
            Context = new CapabilityDecisionContext(),
            Catalog = Snapshot(Descriptor("test:lookup")),
            Budgets = DecisionBudgets.Default,
            BaseExecutionContext = new CapabilityExecutionContext { OutputQueue = queue },
        }))
        {
            if (evt.Kind == DecisionEventKind.Output)
                outputs.Add((OutputItem)evt.Payload!);
        }

        var emitted = Assert.Single(outputs);
        Assert.Equal("稍等", emitted.Payload);
        Assert.True(emitted.Sequence > 0);
        Assert.Single(queue.ReadSince(null, 0));
    }
}
