using openLuo.Capabilities.Core;
using openLuo.Protocol;
using openLuo.Server;
using Xunit;

namespace openLuo.Tests.Security;

/// <summary>
/// 客户端可覆盖的回合预算必须被服务端夹取（§4.9）：预算是资源控制，
/// 不能让单个连接用超大 decision 数 / 数小时 deadline 占住回合并发额度。
/// </summary>
public sealed class TurnBudgetPolicyTests
{
    [Fact]
    public void From_NullDto_ReturnsNullSoRuntimeAppliesConfigDefault()
    {
        // 关键语义："没传"不能变成"传了内核默认值"——否则 agent.chatRoundTimeoutSeconds 永远不会生效。
        Assert.Null(TurnBudgetPolicy.From(null));
    }

    [Fact]
    public void From_UnspecifiedFields_FallBackToKernelDefaults()
    {
        var budgets = TurnBudgetPolicy.From(new DecisionBudgetsDto { MaxDecisions = 3 })!;

        Assert.Equal(3, budgets.MaxDecisions);
        Assert.Equal(DecisionBudgets.Default.MaxToolCallsPerDecision, budgets.MaxToolCallsPerDecision);
        Assert.Equal(DecisionBudgets.Default.OverallDeadline, budgets.OverallDeadline);
        Assert.Equal(DecisionBudgets.Default.StepIdleTimeout, budgets.StepIdleTimeout);
    }

    [Fact]
    public void From_ValuesAboveCeiling_AreClamped()
    {
        var budgets = TurnBudgetPolicy.From(new DecisionBudgetsDto
        {
            MaxDecisions = 10_000,
            MaxToolCallsPerDecision = 10_000,
            MaxConcurrentTools = 10_000,
            OverallDeadlineSec = 86_400,
            StepIdleTimeoutSec = 86_400,
            MaxToolRetries = 10_000
        })!;

        Assert.Equal(TurnBudgetPolicy.MaxDecisionsCeiling, budgets.MaxDecisions);
        Assert.Equal(TurnBudgetPolicy.MaxToolCallsPerDecisionCeiling, budgets.MaxToolCallsPerDecision);
        Assert.Equal(TurnBudgetPolicy.MaxConcurrentToolsCeiling, budgets.MaxConcurrentTools);
        Assert.Equal(TurnBudgetPolicy.OverallDeadlineSecondsCeiling, budgets.OverallDeadline.TotalSeconds);
        Assert.Equal(TurnBudgetPolicy.StepIdleTimeoutSecondsCeiling, budgets.StepIdleTimeout.TotalSeconds);
        Assert.Equal(TurnBudgetPolicy.MaxToolRetriesCeiling, budgets.MaxToolRetries);
    }

    [Fact]
    public void From_StepIdleTimeoutZero_CannotDisablePerToolTimeout()
    {
        // 客户端可以把单步超时调小，但**不能**关掉（0 在内核里表示关闭）——
        // 否则一个挂死的工具就能无限阻塞回合。
        var budgets = TurnBudgetPolicy.From(new DecisionBudgetsDto { StepIdleTimeoutSec = 0 })!;

        Assert.Equal(1, budgets.StepIdleTimeout.TotalSeconds);
    }

    [Fact]
    public void From_CanTightenBelowDefaults()
    {
        // 收紧总是允许的（预算不是只准放宽的配额）
        var budgets = TurnBudgetPolicy.From(new DecisionBudgetsDto
        {
            MaxDecisions = 1,
            OverallDeadlineSec = 5,
            MaxToolRetries = 0
        })!;

        Assert.Equal(1, budgets.MaxDecisions);
        Assert.Equal(5, budgets.OverallDeadline.TotalSeconds);
        Assert.Equal(0, budgets.MaxToolRetries);
    }
}
