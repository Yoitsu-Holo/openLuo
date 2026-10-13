using openLuo.Capabilities.Core;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>
/// 客户端可覆盖的回合预算（wire <c>DecisionBudgetsDto</c>）→ 内核 <c>DecisionBudgets</c>。
/// <para>
/// 服务端是权威：客户端给的值都会被**夹到**安全区间。预算是资源控制——不设上限的话，
/// 一个连接就能用 1000 次决策 / 数小时的 deadline 占住运行时的回合并发额度。
/// 未提供的字段用内核默认（<see cref="DecisionBudgets.Default"/>）。
/// </para>
/// <para>
/// 该类型在 Server 而不是内核：内核不该关心"谁在传预算"，夹取属于入口治理（§4.9）。
/// </para>
/// </summary>
public static class TurnBudgetPolicy
{
    // 上限（夹取区间）。名字带 Ceiling 以免与内核默认值混淆。
    public const int MaxDecisionsCeiling = 32;
    public const int MaxToolCallsPerDecisionCeiling = 20;
    public const int MaxConcurrentToolsCeiling = 8;
    public const double OverallDeadlineSecondsCeiling = 3600;
    public const double StepIdleTimeoutSecondsCeiling = 900;
    public const int MaxToolRetriesCeiling = 5;

    /// <summary>
    /// 映射并夹取。<paramref name="dto"/> 为 null 时返回 null——交给运行时用配置默认
    /// （<c>agent.chatRoundTimeoutSeconds</c>），不要把"没传"变成"传了内核默认值"。
    /// </summary>
    public static DecisionBudgets? From(DecisionBudgetsDto? dto)
    {
        if (dto is null)
            return null;

        var defaults = DecisionBudgets.Default;
        return new DecisionBudgets
        {
            MaxDecisions = Clamp(dto.MaxDecisions, defaults.MaxDecisions, 1, MaxDecisionsCeiling),
            MaxToolCallsPerDecision = Clamp(dto.MaxToolCallsPerDecision, defaults.MaxToolCallsPerDecision, 1, MaxToolCallsPerDecisionCeiling),
            MaxConcurrentTools = Clamp(dto.MaxConcurrentTools, defaults.MaxConcurrentTools, 1, MaxConcurrentToolsCeiling),
            OverallDeadline = TimeSpan.FromSeconds(Clamp(dto.OverallDeadlineSec, defaults.OverallDeadline.TotalSeconds, 1, OverallDeadlineSecondsCeiling)),
            // 单步超时不给"关闭"档（0 在内核里表示关闭）：客户端可调小，但不能让工具无限阻塞回合。
            StepIdleTimeout = TimeSpan.FromSeconds(Clamp(dto.StepIdleTimeoutSec, defaults.StepIdleTimeout.TotalSeconds, 1, StepIdleTimeoutSecondsCeiling)),
            MaxToolRetries = Clamp(dto.MaxToolRetries, defaults.MaxToolRetries, 0, MaxToolRetriesCeiling)
        };
    }

    private static int Clamp(int? value, int fallback, int min, int max) =>
        value is null ? fallback : Math.Clamp(value.Value, min, max);

    private static double Clamp(double? value, double fallback, double min, double max) =>
        value is null ? fallback : Math.Clamp(value.Value, min, max);
}
