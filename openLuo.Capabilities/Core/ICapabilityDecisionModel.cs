using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Core;

/// <summary>
/// 模型决策结果（D2/D17，统一消息流）。
/// Messages：模型本轮产出的消息项（Respond=最终回复结束回合；Inqueue=入队即时推送；
/// Discard=仅回填上下文）。Calls：工具调用。
/// </summary>
public sealed class CapabilityDecision
{
    public IReadOnlyList<FlowItem> Messages { get; init; } = [];
    public IReadOnlyList<CapabilityCall> Calls { get; init; } = [];
}

/// <summary>
/// 决策模型端口（D18）。openLuo.Capabilities 通过本接口获得"下一步做什么"，
/// 不依赖具体 LLM SDK；LLM 实现在 openLuo.Capabilities.Llm。
/// </summary>
public interface ICapabilityDecisionModel
{
    Task<CapabilityDecision> DecideAsync(
        CapabilityDecisionContext context,
        CancellationToken ct = default);
}
