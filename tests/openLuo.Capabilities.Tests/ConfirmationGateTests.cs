using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using NSubstitute;
using Xunit;

namespace openLuo.Capabilities.Tests;

/// <summary>高危能力确认闸门：未确认 → 拒绝且不执行；已确认 → 正常执行。</summary>
public sealed class ConfirmationGateTests
{
    private static CapabilityDescriptor Descriptor(string canonicalId, bool requiresConfirmation) => new()
    {
        CanonicalId = canonicalId,
        ModelToolName = canonicalId.Replace(':', '_'),
        Kind = CapabilityKind.Builtin,
        ProviderId = "test",
        Summary = "高危操作",
        RequiresConfirmation = requiresConfirmation,
    };

    private static CapabilityCatalogSnapshot Snapshot(params CapabilityDescriptor[] descriptors) => new()
    {
        ByCanonicalId = descriptors.ToDictionary(d => d.CanonicalId, StringComparer.OrdinalIgnoreCase),
        ModelNameToCanonicalId = descriptors.ToDictionary(d => d.ModelToolName, d => d.CanonicalId, StringComparer.OrdinalIgnoreCase),
        CanonicalIdToModelName = descriptors.ToDictionary(d => d.CanonicalId, d => d.ModelToolName, StringComparer.OrdinalIgnoreCase),
    };

    private sealed class FakeGate(bool approve) : IConfirmationGate
    {
        public int Calls { get; private set; }
        public ConfirmationRequest? Last { get; private set; }

        public Task<bool> RequestAsync(ConfirmationRequest request, CancellationToken ct = default)
        {
            Calls++;
            Last = request;
            return Task.FromResult(approve);
        }
    }

    private static CapabilityCall Call(string canonicalId = "world:lock") =>
        new() { InvocationId = "i1", CanonicalId = canonicalId, ParentDecisionId = "d1" };

    [Fact]
    public async Task DeniedConfirmation_RejectsWithoutInvoking()
    {
        var invoker = Substitute.For<ICapabilityInvoker>();
        var gate = new FakeGate(approve: false);
        var dispatcher = new DefaultCapabilityDispatcher(
            invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction(), confirmationGate: gate);

        var batch = await dispatcher.ExecuteBatchAsync(
            [Call()], new CapabilityDecisionContext(), Snapshot(Descriptor("world:lock", true)),
            new CapabilityExecutionContext { SessionId = "s1", TurnId = "t1" });

        var result = Assert.Single(batch.Results);
        Assert.False(result.Success);
        Assert.Equal(CapabilityStatus.Rejected, result.Status);
        Assert.Equal(1, gate.Calls);
        Assert.Equal("world:lock", gate.Last!.CanonicalId);
        await invoker.DidNotReceive().InvokeAsync(
            Arg.Any<CapabilityCall>(), Arg.Any<CapabilityExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApprovedConfirmation_InvokesCapability()
    {
        var invoker = Substitute.For<ICapabilityInvoker>();
        invoker.InvokeAsync(Arg.Any<CapabilityCall>(), Arg.Any<CapabilityExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new CapabilityResult { InvocationId = "i1", Success = true, Status = CapabilityStatus.Ok, Text = "done" });

        var gate = new FakeGate(approve: true);
        var dispatcher = new DefaultCapabilityDispatcher(
            invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction(), confirmationGate: gate);

        var batch = await dispatcher.ExecuteBatchAsync(
            [Call()], new CapabilityDecisionContext(), Snapshot(Descriptor("world:lock", true)),
            new CapabilityExecutionContext { SessionId = "s1", TurnId = "t1" });

        Assert.True(Assert.Single(batch.Results).Success);
        Assert.Equal(1, gate.Calls);
        await invoker.Received(1).InvokeAsync(
            Arg.Any<CapabilityCall>(), Arg.Any<CapabilityExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GateNotConsulted_WhenConfirmationNotRequired()
    {
        var invoker = Substitute.For<ICapabilityInvoker>();
        invoker.InvokeAsync(Arg.Any<CapabilityCall>(), Arg.Any<CapabilityExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new CapabilityResult { InvocationId = "i1", Success = true, Status = CapabilityStatus.Ok });

        var gate = new FakeGate(approve: false);
        var dispatcher = new DefaultCapabilityDispatcher(
            invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction(), confirmationGate: gate);

        var batch = await dispatcher.ExecuteBatchAsync(
            [Call("world:state.read")], new CapabilityDecisionContext(), Snapshot(Descriptor("world:state.read", false)),
            new CapabilityExecutionContext());

        Assert.True(Assert.Single(batch.Results).Success);
        Assert.Equal(0, gate.Calls);   // 非高危：不询问
    }
}
