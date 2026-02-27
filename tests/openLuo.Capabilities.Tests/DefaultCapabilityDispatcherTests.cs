using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using Xunit;

namespace openLuo.Capabilities.Tests;

public class DefaultCapabilityDispatcherTests
{
    private static CapabilityDescriptor MakeDescriptor(
        string canonicalId,
        bool parallelSafe = true,
        SideEffectClass sideEffect = SideEffectClass.ReadOnly,
        params string[] resources) => new()
    {
        CanonicalId = canonicalId,
        ModelToolName = canonicalId.Replace(':', '_'),
        Kind = CapabilityKind.Builtin,
        ProviderId = "test",
        ParallelSafe = parallelSafe,
        SideEffect = sideEffect,
        AccessesResources = resources
    };

    private static CapabilityCatalogSnapshot Snapshot(params CapabilityDescriptor[] descriptors) =>
        new()
        {
            ByCanonicalId = descriptors.ToDictionary(d => d.CanonicalId, StringComparer.OrdinalIgnoreCase),
            ModelNameToCanonicalId = descriptors.ToDictionary(d => d.ModelToolName, d => d.CanonicalId, StringComparer.OrdinalIgnoreCase),
            CanonicalIdToModelName = descriptors.ToDictionary(d => d.CanonicalId, d => d.ModelToolName, StringComparer.OrdinalIgnoreCase)
        };

    private static CapabilityCall Call(string invocationId, string canonicalId) => new()
    {
        InvocationId = invocationId,
        IdempotencyKey = $"key-{invocationId}",
        CanonicalId = canonicalId,
        ParentDecisionId = "d1"
    };

    private static CapabilityExecutionContext Ctx() => new()
    {
        SubjectId = "subject-1",
        SessionId = "s1",
        TurnId = "t1",
        SnapshotVersion = 1,
        ReadSnapshot = NullReadSnapshot.Instance
    };

    private sealed class NullReadSnapshot : IReadOnlySnapshot
    {
        public static readonly NullReadSnapshot Instance = new();
        public StateSnapshot? Get(string subjectId) => null;
        public object? GetValue(string subjectId, string resourcePath) => null;
        public long GetVersion(string subjectId) => 0;
    }

    [Fact]
    public async Task ExecuteBatch_TwoParallelSafeCalls_RunsBoth()
    {
        var invoker = new StubInvoker();
        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());

        var result = await dispatcher.ExecuteBatchAsync(
            [Call("inv-1", "test:a"), Call("inv-2", "test:b")],
            new CapabilityDecisionContext(),
            Snapshot(MakeDescriptor("test:a"), MakeDescriptor("test:b")),
            Ctx());

        Assert.False(result.Rejected);
        Assert.Equal(2, result.Results.Count);
        Assert.All(result.Results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task ExecuteBatch_ConflictingMutations_RejectsWholeBatch()
    {
        var invoker = new StubInvoker();
        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());

        var result = await dispatcher.ExecuteBatchAsync(
            [Call("inv-1", "test:m1"), Call("inv-2", "test:m2")],
            new CapabilityDecisionContext(),
            Snapshot(
                MakeDescriptor("test:m1", sideEffect: SideEffectClass.Mutation, resources: "world:state:mood"),
                MakeDescriptor("test:m2", sideEffect: SideEffectClass.Mutation, resources: "world:state:mood")),
            Ctx());

        Assert.True(result.Rejected);
        Assert.NotNull(result.RejectionReason);
    }

    [Fact]
    public async Task ExecuteBatch_UnknownCapability_Rejects()
    {
        var invoker = new StubInvoker();
        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());

        var result = await dispatcher.ExecuteBatchAsync(
            [Call("inv-1", "test:unknown")],
            new CapabilityDecisionContext(),
            Snapshot(),
            Ctx());

        Assert.True(result.Rejected);
        Assert.Contains("unknown capability", result.RejectionReason);
    }

    [Fact]
    public async Task ExecuteBatch_NonParallelSafe_ExecutesSerially()
    {
        var invoker = new StubInvoker();
        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());

        var result = await dispatcher.ExecuteBatchAsync(
            [Call("inv-1", "test:a"), Call("inv-2", "test:b")],
            new CapabilityDecisionContext(),
            Snapshot(
                MakeDescriptor("test:a", parallelSafe: false),
                MakeDescriptor("test:b")),
            Ctx());

        Assert.False(result.Rejected);
        Assert.Equal(2, result.Results.Count);
    }

    [Fact]
    public async Task ExecuteBatch_InvokerThrows_ReturnsFailedResult()
    {
        var invoker = new ThrowingInvoker();
        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());

        var result = await dispatcher.ExecuteBatchAsync(
            [Call("inv-1", "test:a")],
            new CapabilityDecisionContext(),
            Snapshot(MakeDescriptor("test:a")),
            Ctx());

        Assert.False(result.Rejected);
        Assert.Single(result.Results);
        Assert.False(result.Results[0].Success);
        Assert.Equal(CapabilityStatus.Failed, result.Results[0].Status);
    }

    private sealed class StubInvoker : ICapabilityInvoker
    {
        public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default) =>
            Task.FromResult(new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Success = true,
                Status = CapabilityStatus.Ok,
                Text = $"ok:{call.CanonicalId}"
            });
    }

    private sealed class ThrowingInvoker : ICapabilityInvoker
    {
        public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }
}
