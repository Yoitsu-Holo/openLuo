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

    [Fact]
    public async Task ExecuteBatch_FlakyInvoker_RetriesInDispatch_UntilSuccess()
    {
        // 工具级重试：第 1 次失败、第 2 次成功 → dispatcher 内自动重放（不经过模型），
        // 最终返回成功，且调用恰好 2 次（成功后不再重放）。这是"多轮调用每张都成功"场景的
        // 核心保证——调度层重试只对连续失败生效，成功即停。
        var invoker = new FlakyInvoker(failuresBeforeSuccess: 1);
        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());

        var result = await dispatcher.ExecuteBatchAsync(
            [Call("inv-1", "test:a")],
            new CapabilityDecisionContext(),   // 默认 Budgets.MaxToolRetries = 2
            Snapshot(MakeDescriptor("test:a")),
            Ctx());

        Assert.False(result.Rejected);
        Assert.Single(result.Results);
        Assert.True(result.Results[0].Success, $"{result.Results[0].Error}");
        Assert.Equal(2, invoker.InvokeCount);   // 1 失败 + 1 重试成功
    }

    [Fact]
    public async Task ExecuteBatch_AlwaysFails_RetriesMaxRetriesTimes_ThenReturnsFailure()
    {
        // 连续失败达到 MaxToolRetries（=2）后停止重放，把失败结果交回上层，不再无限重试。
        var invoker = new AlwaysFailInvoker();
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
        // 原始 1 次 + MaxToolRetries 次重放
        Assert.Equal(3, invoker.InvokeCount);
    }

    [Fact]
    public async Task ExecuteBatch_EachCallSucceeds_NoRetry_NoError()
    {
        // "获取 10 张图每张都成功"：多个调用各自成功，调度层不得触发任何重试/报错。
        var invoker = new StubInvoker();
        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());

        var calls = Enumerable.Range(1, 10).Select(i => Call($"inv-{i}", $"test:a{i}")).ToArray();
        var result = await dispatcher.ExecuteBatchAsync(
            calls,
            new CapabilityDecisionContext(),
            Snapshot(calls.Select(c => MakeDescriptor(c.CanonicalId)).ToArray()),
            Ctx());

        Assert.False(result.Rejected);
        Assert.Equal(10, result.Results.Count);
        Assert.All(result.Results, r => Assert.True(r.Success));
        Assert.Equal(10, invoker.InvokeCount);   // 每张只调 1 次，无重放
    }

    [Fact]
    public async Task ExecuteBatch_RejectedCall_DoesNotRetry()
    {
        // 回归：Rejected（参数/业务校验失败，确定性错误）不得进入工具级重试——
        // 重放相同参数必然再次失败（日志曾见 share_song 缺 id 被重试 3 次的浪费）。
        var invoker = new RejectingInvoker();
        var dispatcher = new DefaultCapabilityDispatcher(invoker, new DefaultCapabilityPolicy(), new InMemoryStateTransaction());

        var result = await dispatcher.ExecuteBatchAsync(
            [Call("inv-1", "test:a")],
            new CapabilityDecisionContext(),   // 默认 MaxToolRetries = 2（若误重试会是 3 次调用）
            Snapshot(MakeDescriptor("test:a")),
            Ctx());

        Assert.False(result.Rejected);
        Assert.Single(result.Results);
        Assert.False(result.Results[0].Success);
        Assert.Equal(CapabilityStatus.Rejected, result.Results[0].Status);
        Assert.Equal(1, invoker.InvokeCount);   // 校验拒绝只调一次，立即回填让模型纠错
    }

    private sealed class RejectingInvoker : ICapabilityInvoker
    {
        public int InvokeCount { get; private set; }
        public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
        {
            InvokeCount++;
            return Task.FromResult(new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Success = false,
                Status = CapabilityStatus.Rejected,
                Error = "id is required"
            });
        }
    }

    private sealed class FlakyInvoker : ICapabilityInvoker
    {
        private readonly int _failuresBeforeSuccess;
        public int InvokeCount { get; private set; }
        public FlakyInvoker(int failuresBeforeSuccess) => _failuresBeforeSuccess = failuresBeforeSuccess;

        public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
        {
            InvokeCount++;
            if (InvokeCount <= _failuresBeforeSuccess)
                return Task.FromResult(new CapabilityResult
                {
                    InvocationId = call.InvocationId,
                    Success = false,
                    Status = CapabilityStatus.Failed,
                    Error = $"flaky failure {InvokeCount}"
                });
            return Task.FromResult(new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Success = true,
                Status = CapabilityStatus.Ok,
                Text = "ok after retry"
            });
        }
    }

    private sealed class AlwaysFailInvoker : ICapabilityInvoker
    {
        public int InvokeCount { get; private set; }
        public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
        {
            InvokeCount++;
            return Task.FromResult(new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Success = false,
                Status = CapabilityStatus.Failed,
                Error = $"always fail {InvokeCount}"
            });
        }
    }

    private sealed class StubInvoker : ICapabilityInvoker
    {
        public int InvokeCount { get; private set; }
        public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
        {
            InvokeCount++;
            return Task.FromResult(new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Success = true,
                Status = CapabilityStatus.Ok,
                Text = $"ok:{call.CanonicalId}"
            });
        }
    }

    private sealed class ThrowingInvoker : ICapabilityInvoker
    {
        public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }
}
