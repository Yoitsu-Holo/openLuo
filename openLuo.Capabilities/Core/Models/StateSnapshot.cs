namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// 领域状态快照（D20）。内核不认识字段含义，只按 SubjectId + 资源路径存值。
/// </summary>
public sealed class StateSnapshot
{
    public string SubjectId { get; init; } = string.Empty;
    public long Version { get; init; }
    public IReadOnlyDictionary<string, object?> Values { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>变更操作类型。</summary>
public enum MutationOp
{
    Set,
    Add,
    Remove,
    Increment
}

/// <summary>
/// 变更意图（D20）。能力执行只"提案"，不直接写状态；提交由内核事务统一校验。
/// </summary>
public sealed class MutationIntent
{
    public string CapabilityId { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public string ResourcePath { get; init; } = string.Empty;
    public MutationOp Op { get; init; } = MutationOp.Set;
    public object? Value { get; init; }
    public string? IdempotencyKey { get; init; }
}

/// <summary>mutation 批次结果状态。</summary>
public enum MutationBatchStatus
{
    Committed,
    Conflict,
    Rejected,
    Partial
}

/// <summary>
/// mutation 批次提交结果（D9）。Committed 时含新快照；Conflict 时含冲突资源路径。
/// </summary>
public sealed class MutationBatchResult
{
    public MutationBatchStatus Status { get; init; } = MutationBatchStatus.Rejected;
    public StateSnapshot? NewSnapshot { get; init; }
    public IReadOnlyList<string> Conflicts { get; init; } = [];
    public string? RejectionReason { get; init; }
}

/// <summary>
/// 状态事务（D20）。内核保证"版本 + 意图 + 原子提交"；字段校验由扩展的 mutation handler 承担。
/// </summary>
public interface IStateTransaction
{
    Task<MutationBatchResult> CommitAsync(
        string subjectId,
        long baseVersion,
        IReadOnlyList<MutationIntent> intents,
        CancellationToken ct = default);
}
