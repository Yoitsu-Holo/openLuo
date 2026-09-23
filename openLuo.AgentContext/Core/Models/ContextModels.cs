using openLuo.Capabilities.Core.Models;

namespace openLuo.AgentContext.Core.Models;

/// <summary>上下文区域（EnhanceMsg 映射，D43）。</summary>
public enum ContextRegion
{
    Identity,
    TimeContext,
    WorldContext,
    SceneState,
    GoalContext,
    LongTermMemory,
    RuntimeRules,
    ConversationHistory,
    CurrentUserInput,
    ToolResults,
    Capabilities
}

/// <summary>贡献来源状态（D42 失败降级）。</summary>
public enum ContextSourceStatus
{
    Ok,
    Unavailable
}

/// <summary>每轮快照构造请求（Contributor 唯一输入，不可变）。</summary>
public sealed class ContextBuildRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string? UserInput { get; init; }
    public IReadOnlyList<object>? UserBlocks { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, object?> Extras { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>单条上下文贡献（Contributor 的只读产出）。</summary>
public sealed class ContextContribution
{
    public string Id { get; init; } = string.Empty;
    public string ContributorId { get; init; } = string.Empty;
    public ContextRegion Region { get; init; } = ContextRegion.WorldContext;
    public string Content { get; init; } = string.Empty;
    public int Priority { get; init; }
    public long TokenEstimate { get; init; }
    public string? Version { get; init; }
}

/// <summary>贡献来源状态（失败降级用）。</summary>
public sealed class ContextSourceState
{
    public string SourceId { get; init; } = string.Empty;
    public ContextSourceStatus Status { get; init; } = ContextSourceStatus.Ok;
    public string? Reason { get; init; }
    public bool Retryable { get; init; }
}

/// <summary>Contributor 的结果（贡献 + 来源状态）。</summary>
public sealed class ContextContributionResult
{
    public ContextSourceState State { get; init; } = new() { SourceId = "unknown", Status = ContextSourceStatus.Ok };
    public IReadOnlyList<ContextContribution> Contributions { get; init; } = [];
}
