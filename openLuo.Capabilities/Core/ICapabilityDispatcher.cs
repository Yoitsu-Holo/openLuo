using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Core;

/// <summary>批次校验结果（D8）。</summary>
public sealed class BatchValidation
{
    public bool Ok { get; init; }
    public string? RejectionReason { get; init; }
    public IReadOnlyList<CapabilityCall>? SerializedCalls { get; init; }   // 需串行的子批次
    public IReadOnlyList<CapabilityCall>? ParallelCalls { get; init; }    // 可并行的调用

    public static BatchValidation OkBatch(
        IReadOnlyList<CapabilityCall> parallel,
        IReadOnlyList<CapabilityCall> serialized) => new()
    {
        Ok = true,
        ParallelCalls = parallel,
        SerializedCalls = serialized
    };

    public static BatchValidation Reject(string reason) => new() { Ok = false, RejectionReason = reason };
}

/// <summary>
/// 能力策略（D7/D8）：模型只能调用目录内能力；参数 schema 校验；并行约束；副作用分级。
/// </summary>
public interface ICapabilityPolicy
{
    BatchValidation ValidateBatch(
        IReadOnlyList<CapabilityCall> calls,
        CapabilityCatalogSnapshot snapshot,
        CapabilityDecisionContext context);
}

/// <summary>批次执行结果（D7/D9/D10）。</summary>
public sealed class BatchExecutionResult
{
    /// <summary>按模型调用顺序排列的结果。</summary>
    public IReadOnlyList<CapabilityResult> Results { get; init; } = [];

    /// <summary>本次批次中本地 mutation 的提交结果（D9）。</summary>
    public MutationBatchResult? MutationOutcome { get; init; }

    /// <summary>批次是否因非法并行被整体拒绝。</summary>
    public bool Rejected { get; init; }
    public string? RejectionReason { get; init; }
}

/// <summary>
/// 能力调度器（D7-D10）：前线快照并行执行、非法并行整批拒绝、mutation intent 收集与原子提交。
/// </summary>
public interface ICapabilityDispatcher
{
    Task<BatchExecutionResult> ExecuteBatchAsync(
        IReadOnlyList<CapabilityCall> calls,
        CapabilityDecisionContext context,
        CapabilityCatalogSnapshot snapshot,
        CapabilityExecutionContext executionContext,
        CancellationToken ct = default);
}
