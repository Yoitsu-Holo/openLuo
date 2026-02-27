using openLuo.AgentContext.Core.Models;

namespace openLuo.AgentContext.Core;

/// <summary>
/// 上下文组装器（D42）：串行调用所有 Contributor、合并贡献、按 Region/Priority 排序、
/// 去重、预算裁剪、合并 SourceStates（Unavailable 保留结构化状态）、生成不可变快照。
/// 不执行工具、不修改状态。
/// </summary>
public interface IContextAssembler
{
    Task<AgentDecisionContext> BuildAsync(
        ContextBuildRequest request,
        CancellationToken ct = default);
}
