namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// 能力统一元数据。字段语义见设计文档 D19。
/// </summary>
public sealed record CapabilityDescriptor
{
    /// <summary>稳定标识，如 "world:inventory.read"。全局唯一。</summary>
    public string CanonicalId { get; init; } = string.Empty;

    /// <summary>当前 Turn 映射给模型的调用名。由目录快照生成。</summary>
    public string ModelToolName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>摘要（注入上下文，短）。</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>何时使用（注入上下文，短）。</summary>
    public string Usage { get; init; } = string.Empty;

    public CapabilityKind Kind { get; init; } = CapabilityKind.Builtin;

    /// <summary>提供方标识：扩展 id / mcp server id / agent id / "core"。</summary>
    public string ProviderId { get; init; } = string.Empty;

    public string Version { get; init; } = "1.0.0";

    public SideEffectClass SideEffect { get; init; } = SideEffectClass.Pure;

    public CompletionPolicy Completion { get; init; } = CompletionPolicy.Continue;

    /// <summary>是否允许与兄弟节点并行执行。</summary>
    public bool ParallelSafe { get; init; } = true;

    /// <summary>原生 tool schema（JSON Schema 对象）。</summary>
    public object InputSchema { get; init; } = new();

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public RiskLevel Risk { get; init; } = RiskLevel.Low;

    public bool RequiresConfirmation { get; init; }

    public IdempotencyKind Idempotency { get; init; } = IdempotencyKind.Unknown;

    /// <summary>访问的资源路径（如 "world:state:mood"），用于并行与冲突检测。</summary>
    public IReadOnlyList<string> AccessesResources { get; init; } = [];
}
