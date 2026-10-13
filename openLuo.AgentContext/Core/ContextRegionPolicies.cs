using openLuo.AgentContext.Core.Models;

namespace openLuo.AgentContext.Core;

/// <summary>区域在预算压力下的保留级别。</summary>
public enum ContextRetention
{
    /// <summary>保底：预算再紧也不裁（被裁会导致模型行为崩坏，如失去人格/平台规则/能力说明）。</summary>
    Pinned,

    /// <summary>可裁：预算不足时按优先级让位，被裁条目记入 <see cref="ContextBudgetSnapshot.DroppedContributions"/>。</summary>
    Droppable,
}

/// <summary>
/// 区域保留策略（D44）。**单一来源**：哪些区域保底、哪些可裁只在这里表态。
/// <para>
/// 为什么按区域而不是按贡献者自报的 Priority：渲染顺序由 <see cref="ContextRegion"/> 枚举序决定，
/// 而旧的裁剪是按枚举序 <c>break</c>——于是排在后面的 Capabilities / RuntimeRules 会被
/// 排在前面的块挤掉（且优先级跨区域完全失效），模型会莫名"忘记"平台规则与能力清单，还无任何信号。
/// 现在：保底集必入，可裁集按 Priority 贪心填充并**跳过**放不下的小项（不再级联丢弃）。
/// </para>
/// 新增 <see cref="ContextRegion"/> 成员时必须在 <see cref="Map"/> 表态，否则 <c>ContextRegionPolicyTests</c> 会红。
/// </summary>
public static class ContextRegionPolicies
{
    private static readonly Dictionary<ContextRegion, ContextRetention> Map = new()
    {
        [ContextRegion.Identity] = ContextRetention.Pinned,            // 人格/身份：角色是否还是本人
        [ContextRegion.TimeContext] = ContextRetention.Pinned,         // 时间：体积极小，缺失会让模型乱猜时间
        [ContextRegion.WorldContext] = ContextRetention.Droppable,
        [ContextRegion.SceneState] = ContextRetention.Droppable,       // 当前场景：可让，先保人格与规则
        [ContextRegion.GoalContext] = ContextRetention.Droppable,
        [ContextRegion.LongTermMemory] = ContextRetention.Droppable,   // 长期记忆：预算紧张时最先让位
        [ContextRegion.RuntimeRules] = ContextRetention.Pinned,        // 平台行为规则
        // 已加载技能的正文：可裁——预算紧张时先丢正文（目录里仍留着 id 与适用时机，模型可以重新加载）
        [ContextRegion.LoadedSkills] = ContextRetention.Droppable,
        [ContextRegion.ConversationHistory] = ContextRetention.Droppable,
        [ContextRegion.CurrentUserInput] = ContextRetention.Pinned,    // 当轮用户输入绝不能丢
        [ContextRegion.ToolResults] = ContextRetention.Pinned,         // 本回合工具结果丢了模型会重复调用
        [ContextRegion.Capabilities] = ContextRetention.Pinned,        // 能力清单说明
    };

    /// <summary>已显式表态的区域集合（守卫测试用）。</summary>
    public static IReadOnlyCollection<ContextRegion> Declared => Map.Keys;

    /// <summary>
    /// 取区域保留级别。未表态的区域按 <see cref="ContextRetention.Pinned"/> 处理——
    /// 失败方向选"宁可超预算也不无声丢弃"（超预算会被 <see cref="ContextBudgetSnapshot.OverBudget"/> 显式记录）。
    /// </summary>
    public static ContextRetention RetentionOf(ContextRegion region) =>
        Map.TryGetValue(region, out var retention) ? retention : ContextRetention.Pinned;
}
