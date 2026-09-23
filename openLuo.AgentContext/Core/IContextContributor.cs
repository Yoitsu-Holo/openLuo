using openLuo.AgentContext.Core.Models;

namespace openLuo.AgentContext.Core;

/// <summary>
/// 上下文贡献者（D42/D43）。每轮快照构造时被串行调用；原子独立、只读查询、
/// 失败独立降级（返回 Unavailable 结构化状态，不阻塞其他贡献）。
/// </summary>
public interface IContextContributor
{
    /// <summary>贡献者唯一标识，如 "memory" / "world:state" / "companion:identity"。</summary>
    string Id { get; }

    Task<ContextContributionResult> ContributeAsync(
        ContextBuildRequest request,
        CancellationToken ct = default);
}
