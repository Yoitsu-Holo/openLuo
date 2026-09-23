namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// 单次能力调用请求。InvocationId 唯一标识本次调用；IdempotencyKey 为稳定键（重试复用）。
/// </summary>
public sealed class CapabilityCall
{
    public string InvocationId { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    /// <summary>模型侧 tool_call id（工具结果作为 tool role 消息回传时使用）。</summary>
    public string? ModelCallId { get; init; }
    /// <summary>模型侧工具名（重建 assistant tool_calls 声明时使用）。</summary>
    public string? ModelToolName { get; init; }
    /// <summary>模型原始 arguments JSON（重建 assistant tool_calls 声明时使用）。</summary>
    public string? RawArgumentsJson { get; init; }
    public int Attempt { get; init; }
    public string ParentDecisionId { get; init; } = string.Empty;
    public string CanonicalId { get; init; } = string.Empty;
    public string[] Args { get; init; } = [];
    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>调用结果状态。</summary>
public enum CapabilityStatus
{
    Ok,
    Failed,
    Conflict,
    Rejected,
    Timeout,
    Cancelled
}

/// <summary>
/// 单次能力调用结果。Text 回填给 LLM；Outputs 为可展示输出；Mutations 为待提交变更意图。
/// </summary>
public sealed class CapabilityResult
{
    public string InvocationId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Error { get; init; }
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Failed;
    public string? Text { get; init; }
    public IReadOnlyList<OutputItem> Outputs { get; init; } = [];
    public IReadOnlyList<MutationIntent> Mutations { get; init; } = [];
    public IReadOnlyList<string> AccessTrace { get; init; } = [];
}
