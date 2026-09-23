using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;

namespace openLuo.AgentContext.Infrastructure;

/// <summary>
/// 默认上下文组装器（D42）：串行调用 Contributor、合并贡献、按 Region/Priority 排序、
/// 合并 SourceStates（Unavailable 保留结构化状态）、生成不可变快照。
/// Contributor 之间禁止依赖，只读同一个 ContextBuildRequest。
/// </summary>
public sealed class DefaultContextAssembler : IContextAssembler
{
    private readonly IReadOnlyList<IContextContributor> _contributors;
    private readonly long _maxTokenBudget;

    public DefaultContextAssembler(
        IEnumerable<IContextContributor> contributors,
        long maxTokenBudget = 24_000)
    {
        _contributors = contributors.ToList();
        _maxTokenBudget = maxTokenBudget;
    }

    public async Task<AgentDecisionContext> BuildAsync(
        ContextBuildRequest request,
        CancellationToken ct = default)
    {
        var contributions = new List<ContextContribution>();
        var sourceStates = new List<ContextSourceState>();

        foreach (var contributor in _contributors)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await contributor.ContributeAsync(request, ct);
                sourceStates.Add(result.State);
                contributions.AddRange(result.Contributions);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单个 Contributor 失败独立降级（D42）
                sourceStates.Add(new ContextSourceState
                {
                    SourceId = contributor.Id,
                    Status = ContextSourceStatus.Unavailable,
                    Reason = ex.Message,
                    Retryable = true
                });
            }
        }

        // 按 Region/Priority 排序 + 预算裁剪
        var ordered = contributions
            .OrderBy(c => c.Region)
            .ThenByDescending(c => c.Priority)
            .ToList();

        var withinBudget = new List<ContextContribution>();
        long used = 0;
        foreach (var contribution in ordered)
        {
            used += Math.Max(1, contribution.TokenEstimate);
            if (used > _maxTokenBudget)
                break;
            withinBudget.Add(contribution);
        }

        return new AgentDecisionContext
        {
            SessionId = request.SessionId,
            TurnId = request.TurnId,
            SnapshotVersion = 1,
            Contributions = withinBudget,
            SourceStates = sourceStates,
            UserInput = request.UserInput,
            UserBlocks = request.UserBlocks,
            Budgets = new()
        };
    }
}
