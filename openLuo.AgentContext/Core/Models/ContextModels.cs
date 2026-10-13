using openLuo.Capabilities.Core.Models;

namespace openLuo.AgentContext.Core.Models;

/// <summary>
/// 上下文区域（EnhanceMsg 映射，D43）。渲染顺序 = 本枚举顺序（`[Region]…[/Region]` 块）。
/// <para>
/// 现状（v1）：实际产出的只有 Identity / TimeContext / SceneState / LongTermMemory /
/// RuntimeRules / Capabilities；其余成员标 `v1 未实现`——它们仍在枚举里（协议面保留），
/// 但宿主当前不产出，<c>KernelPrompt</c> 也已按此措辞告知模型"不是每轮都有所有块"。
/// </para>
/// </summary>
public enum ContextRegion
{
    Identity,
    TimeContext,
    /// <summary>v1 未实现：仅作 <see cref="ContextContribution.Region"/> 的默认值，无产出者。</summary>
    WorldContext,
    SceneState,
    /// <summary>v1 未实现：目标系统未落地。</summary>
    GoalContext,
    LongTermMemory,
    RuntimeRules,
    /// <summary>本对话已加载技能的正文（引用展开，D30）；正文由技能库在构造请求时提供。</summary>
    LoadedSkills,
    /// <summary>v1 未实现：历史经 <c>AgentDecisionContext.Conversation</c> 传递，不占贡献区。</summary>
    ConversationHistory,
    /// <summary>v1 未实现：当轮输入走 <c>AgentDecisionContext.UserInput</c>。</summary>
    CurrentUserInput,
    /// <summary>v1 未实现：工具结果以 tool 消息回填对话。</summary>
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
    /// <summary>会话键（多端共享同一段对话的支点）；缺省等于 <see cref="SessionId"/>。</summary>
    public string ConversationId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string? UserInput { get; init; }
    public IReadOnlyList<object>? UserBlocks { get; init; }

    /// <summary>
    /// 说话人的**稳定身份**（平台用户 id，如 QQ 号）。为空 = 平台没提供：
    /// 归因回退到 <see cref="SubjectId"/>、记忆退回 `(会话, 角色)` 分区，即改造前的行为。
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>会话内线程标识（群内每话题/每用户）；为空表示不切分线程。</summary>
    public string? ThreadId { get; init; }

    /// <summary>被 @ 的目标（userId 或角色 id），供模型知道这是定向发言。</summary>
    public IReadOnlyList<string> Mentions { get; init; } = [];

    public DateTimeOffset ReceivedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, object?> Extras { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 取本请求在给定域下的键；缺必要标识时返回 null（例：没有 <see cref="UserId"/> 就取不到 User 域）。
    /// 键形状由 <see cref="ContextDomainKeys"/> 唯一定义。
    /// </summary>
    public ContextDomainKey? DomainKey(ContextScope scope) => scope switch
    {
        ContextScope.Global => ContextDomainKeys.Global(SubjectId),
        ContextScope.Session => ContextDomainKeys.Session(
            string.IsNullOrWhiteSpace(ConversationId) ? SessionId : ConversationId),
        ContextScope.Thread => string.IsNullOrWhiteSpace(ThreadId)
            ? null
            : ContextDomainKeys.Thread(
                string.IsNullOrWhiteSpace(ConversationId) ? SessionId : ConversationId, ThreadId),
        ContextScope.User => string.IsNullOrWhiteSpace(UserId)
            ? null
            : ContextDomainKeys.User(SubjectId, UserId),
        _ => null,
    };
}

/// <summary>单条上下文贡献（Contributor 的只读产出）。</summary>
/// <remarks>
/// 这里**没有** token 字段（预算由装配器用 <see cref="ITokenCounter"/> 统一度量），
/// 也**没有**版本字段（全局块的失效由贡献者的 <see cref="IContextContributor.CurrentVersion"/> 探针负责，
/// 见 D46——曾经有一个零消费者的 `Version` 字段，已删）。
/// 早期版本让贡献者自报 <c>TokenEstimate</c>（各处填 <c>Content.Length / 4</c>），
/// 对中文低估约 4 倍且与实际模型无关——预算因此形同虚设，故删除该字段（D44）。
/// </remarks>
public sealed class ContextContribution
{
    public string Id { get; init; } = string.Empty;
    public string ContributorId { get; init; } = string.Empty;
    public ContextRegion Region { get; init; } = ContextRegion.WorldContext;
    public string Content { get; init; } = string.Empty;
    public int Priority { get; init; }
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
