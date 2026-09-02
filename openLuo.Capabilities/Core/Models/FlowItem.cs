namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// 消息去向模式（统一消息流，替代 FinalText/InternalText/OutputVisibility 三处散落控制）。
/// 模型只输出"文本 + tool_calls"，去向由工程归类：
/// - 无 tool_call 的文本 → Respond（结束回合并发送）
/// - 伴随 tool_call 的文本 → Inqueue（入队即时推送，群聊中途反馈）
/// - 工具内部思考（plan 类，Outputs 为空仅 Text 回填）→ Discard 语义（invoker 层）
/// </summary>
public enum FlowMode
{
    /// <summary>内部流转：仅回填上下文，不推送。</summary>
    Discard,

    /// <summary>待回复队列：入队即时推送（可配开关），不结束回合。</summary>
    Inqueue,

    /// <summary>最终回复：结束回合并发送。</summary>
    Respond
}

/// <summary>统一消息流项：去向（Mode）+ 载荷类型（Kind）+ 内容。</summary>
public sealed record FlowItem
{
    public FlowMode Mode { get; init; } = FlowMode.Discard;
    public ReplyItemKind Kind { get; init; } = ReplyItemKind.Text;
    public object Payload { get; init; } = string.Empty;
    public string SourceCapability { get; init; } = string.Empty;
}
