namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// Workflow 图定义（D13）。黑盒能力：Agent 只调用 run_workflow(workflowId, input)，
/// 不控制内部节点。内部由确定性 runner 执行。
/// </summary>
public sealed class WorkflowDefinition
{
    public string Id { get; init; } = string.Empty;       // "world:gift.accept"
    public string Description { get; init; } = string.Empty;
    public string StartNodeId { get; init; } = string.Empty;
    public int MaxSteps { get; init; } = 16;
    public IReadOnlyList<WorkflowNode> Nodes { get; init; } = [];
    public IReadOnlyList<WorkflowEdge> Edges { get; init; } = [];
}

public sealed class WorkflowNode
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = "step";           // step | terminal
    public string? HandlerId { get; init; }               // 节点执行器标识
    public string Description { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> InputMap { get; init; } = new Dictionary<string, string>();
}

public sealed class WorkflowEdge
{
    public string FromNodeId { get; init; } = string.Empty;
    public string ToNodeId { get; init; } = string.Empty;
    public string? Guard { get; init; }                   // 语义守卫描述（由 handler/runner 评估）
}

public sealed class WorkflowRunRequest
{
    public string WorkflowId { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Input { get; init; } = new Dictionary<string, object?>();
    public string? IdempotencyKey { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public sealed class WorkflowRunResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string TerminalNodeId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Outputs { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyList<string> Steps { get; init; } = [];
}

/// <summary>工作流节点执行器：由扩展注册，按 HandlerId 分发。</summary>
public interface IWorkflowNodeHandler
{
    string HandlerId { get; }
    Task<WorkflowNodeResult> ExecuteAsync(
        WorkflowNode node,
        WorkflowRunRequest request,
        IReadOnlyDictionary<string, object?> state,
        CancellationToken ct = default);
}

public sealed class WorkflowNodeResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public IReadOnlyDictionary<string, object?> Outputs { get; init; } = new Dictionary<string, object?>();
    public string? NextNodeId { get; init; }
}

/// <summary>Workflow 运行时：黑盒执行（D13）。</summary>
public interface IWorkflowRunner
{
    void Register(WorkflowDefinition definition);
    Task<WorkflowRunResult> RunAsync(WorkflowRunRequest request, CancellationToken ct = default);
}
