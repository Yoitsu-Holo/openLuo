using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Core;

/// <summary>
/// 决策循环 → 会话上下文 的桥接端口（D7 前线快照推进）。
/// openLuo.Capabilities 不依赖 openLuo.AgentContext；由宿主实现本端口
/// （宿主侧把工具结果应用到 AgentContextSession，生成 Snapshot N+1）。
/// </summary>
public interface IContextUpdater
{
    /// <summary>把一批工具结果应用到会话上下文，返回下一轮决策上下文。</summary>
    Task<CapabilityDecisionContext> ApplyToolResultsAsync(
        string sessionId,
        string turnId,
        IReadOnlyList<CapabilityCall> calls,
        IReadOnlyList<CapabilityResult> results,
        CancellationToken ct = default);
}
