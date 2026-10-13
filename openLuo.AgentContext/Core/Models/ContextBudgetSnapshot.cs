namespace openLuo.AgentContext.Core.Models;

/// <summary>被预算裁掉的贡献（可观测：让"悄悄丢上下文"变成可查询事实）。</summary>
/// <param name="Id">贡献 id。</param>
/// <param name="ContributorId">贡献者 id。</param>
/// <param name="Region">所属区域。</param>
/// <param name="Priority">贡献者自报优先级。</param>
/// <param name="Tokens">该条占用的估算 token 数。</param>
public sealed record DroppedContribution(
    string Id,
    string ContributorId,
    ContextRegion Region,
    int Priority,
    int Tokens);

/// <summary>
/// 本轮快照的预算账目（D44）。由装配器（系统块）与会话（对话）分别填充。
/// <para>
/// 设计要点：预算**永远不静默**。要么在限额内，要么 <see cref="OverBudget"/> 为真、
/// 且被裁掉的每条都能在 <see cref="DroppedContributions"/> 里查到；对话历史被裁的条数记在
/// <see cref="ConversationTurnsDropped"/>。
/// </para>
/// </summary>
public sealed record ContextBudgetSnapshot
{
    /// <summary>本轮采用的 token 上限。</summary>
    public int MaxTokens { get; init; }

    /// <summary>系统块（含保底超支部分）实际占用。</summary>
    public int SystemTokens { get; init; }

    /// <summary>对话历史实际占用。</summary>
    public int ConversationTokens { get; init; }

    /// <summary>因预算不足而未纳入历史的对话条数（从最旧一侧截断）。</summary>
    public int ConversationTurnsDropped { get; init; }

    /// <summary>被裁掉的系统块贡献。</summary>
    public IReadOnlyList<DroppedContribution> DroppedContributions { get; init; } = [];

    /// <summary>保底块自身已超上限（此时不再接收任何可裁块，且系统块合计超过 <see cref="MaxTokens"/>）。</summary>
    public bool OverBudget { get; init; }

    /// <summary>本轮 token 合计（系统块 + 对话）。</summary>
    public int TotalTokens => SystemTokens + ConversationTokens;

    /// <summary>尚未填充的初值。</summary>
    public static ContextBudgetSnapshot Empty { get; } = new();
}
