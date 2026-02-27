using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Core;

/// <summary>
/// 模型决策结果（D2/D17）。
/// FinalText：无 tool_calls 的非空文本 → 最终回复；
/// InternalText：伴随 tool_calls 的文本 → 内部过程文本，不进公共输出。
/// </summary>
public sealed class CapabilityDecision
{
    public string? FinalText { get; init; }
    public IReadOnlyList<CapabilityCall> Calls { get; init; } = [];
    public string? InternalText { get; init; }
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
