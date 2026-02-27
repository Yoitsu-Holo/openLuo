# 02 · 内核契约

> 内核 = openLuo.Foundation + openLuo.Capabilities + openLuo.AgentContext 中与领域无关的部分。
> 内核不知道 RPG/桌宠/角色/礼物等任何领域语义。

## 1. 内核能力清单（D33）

`core:` 命名空间由内核保留，扩展不得注册：

```text
core:load_skill            加载 Skill 完整指导（会话缓存）
core:unload_skill          卸载 Skill 完整指导
core:list_loaded_skills    列出已加载 Skill
core:inspect_capabilities  查询当前可见能力目录（摘要 + namespace）
core:delegate_agent        委托远程 Agent（A2A 适配入口）
core:list_mcp_servers      列出已连接 MCP server
core:list_mcp_tools        列出 MCP tools
```

## 2. CapabilityDescriptor（D19，完整字段）

```csharp
public enum CapabilityKind { Builtin, Mcp, Workflow, RemoteAgent }
public enum SideEffectClass { Pure, ReadOnly, External, Mutation, Delegation, Terminal }
public enum CompletionPolicy { Continue, MayComplete, Terminal }
public enum OutputVisibility { Silent, Replyable, Public }
public enum RiskLevel { Low, Medium, High }
public enum IdempotencyKind { Idempotent, NonIdempotent, Unknown }

public sealed class CapabilityDescriptor
{
    public string CanonicalId { get; init; }        // "world:inventory.read"
    public string ModelToolName { get; init; }      // 每轮由快照映射
    public string DisplayName { get; init; }
    public string Summary { get; init; }            // 注入上下文（短）
    public string Usage { get; init; }              // 何时使用（短）
    public CapabilityKind Kind { get; init; }
    public string ProviderId { get; init; }         // 扩展 id / mcp server id / agent id
    public string Version { get; init; }
    public SideEffectClass SideEffect { get; init; }
    public CompletionPolicy Completion { get; init; }
    public OutputVisibility Visibility { get; init; }
    public bool ParallelSafe { get; init; }
    public object InputSchema { get; init; }        // JSON Schema（LLM tool 声明）
    public IReadOnlyList<string> Aliases { get; init; }
    public RiskLevel Risk { get; init; }
    public bool RequiresConfirmation { get; init; }
    public IdempotencyKind Idempotency { get; init; }
    public IReadOnlyList<string> AccessesResources { get; init; }
}
```

## 3. 能力调用与结果

```csharp
public sealed class CapabilityCall
{
    public string InvocationId { get; init; }       // 本次调用唯一
    public string IdempotencyKey { get; init; }     // 稳定键（重试复用）
    public int Attempt { get; init; }               // 重试次数
    public string ParentDecisionId { get; init; }   // 所属决策轮
    public string CanonicalId { get; init; }
    public string[] Args { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; }
}

public sealed class CapabilityResult
{
    public string InvocationId { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }             // 结构化错误/失败原因
    public CapabilityStatus Status { get; init; }   // Ok | Failed | Conflict | Rejected | Timeout | Cancelled
    public string? Text { get; init; }              // 回填给 LLM 的 tool 结果文本
    public IReadOnlyList<OutputItem>? Outputs { get; init; }   // Replyable/Public 输出
    public IReadOnlyList<MutationIntent>? Mutations { get; init; }
    public IReadOnlyList<string>? AccessTrace { get; init; }
}
```

## 4. 输出项与输出管道（D3-D6）

```csharp
public enum ReplyItemKind { Text, Image, Audio, File, Card, Asset }

public sealed class OutputItem
{
    public string Id { get; init; }
    public ReplyItemKind Kind { get; init; }
    public object Payload { get; init; }            // 文本/字节/引用
    public string SourceCapability { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string Fingerprint { get; init; }        // 当前 Turn 去重（D3）
    public long Sequence { get; init; }             // 会话内单调递增
}
```

```csharp
public interface IOutputQueue
{
    Task EnqueueAsync(OutputItem item, CancellationToken ct = default);
    IAsyncEnumerable<OutputItem> ReadAsync(CancellationToken ct = default);  // 顺序消费
    Task AckAsync(string itemId, CancellationToken ct = default);            // 发送成功
    Task FailAsync(string itemId, bool permanent, CancellationToken ct = default);
}
```

发送规则：

- 平台适配层订阅 `IOutputQueue`，收到即可回复（D50）
- 同一会话/频道内按 Sequence 顺序发送：后一条等待前一条完成（D4）
- 失败：有限重试（MaxToolRetries 或独立重试策略）→ 放弃 + 固定失败消息（占用原位置）→ 继续（D5）
- 队列仅内存（D6）
- 当前 Turn 内 fingerprint 去重（D3）

## 5. 状态快照与 mutation（D20）

```csharp
public sealed class StateSnapshot
{
    public string SubjectId { get; init; }
    public long Version { get; init; }
    public IReadOnlyDictionary<string, object?> Values { get; init; }
}

public sealed class MutationIntent
{
    public string CapabilityId { get; init; }
    public string SubjectId { get; init; }
    public string ResourcePath { get; init; }       // "mood" / "relationship" / "inventory"
    public MutationOp Op { get; init; }             // Set | Add | Remove | Increment
    public object? Value { get; init; }
    public string? IdempotencyKey { get; init; }
}

public enum MutationBatchStatus { Committed, Conflict, Rejected, Partial }

public sealed class MutationBatchResult
{
    public MutationBatchStatus Status { get; init; }
    public StateSnapshot? NewSnapshot { get; init; }       // Committed 时
    public IReadOnlyList<string> Conflicts { get; init; }  // Conflict 时
    public string? RejectionReason { get; init; }
}

public interface IStateTransaction
{
    Task<MutationBatchResult> CommitAsync(
        string subjectId,
        long baseVersion,
        IReadOnlyList<MutationIntent> intents,
        CancellationToken ct = default);
}
```

字段校验（mutable/clamp/maxDelta）由扩展的 StateMutationHandler 承担，内核不感知字段含义（D20）。

## 6. 决策循环（D18/D41）

```csharp
public interface ICapabilityDecisionModel
{
    Task<CapabilityDecision> DecideAsync(
        CapabilityDecisionContext context,
        CancellationToken ct = default);
}

public sealed class CapabilityDecision
{
    public string? FinalText { get; init; }              // 无 tool_call 的非空文本 = 最终回复（D2）
    public IReadOnlyList<CapabilityCall> Calls { get; init; }
    public string? InternalText { get; init; }           // 伴随 tool_call 的文本 = 内部过程（D17）
}

public interface ICapabilityDecisionLoop
{
    Task<DecisionLoopResult> RunAsync(
        DecisionLoopRequest request,
        CancellationToken ct = default);
}
```

终止条件（D41 配套）：

1. 模型返回无 tool_calls 的非空文本 → 最终回复
2. 达到 MaxDecisions（8）
3. 达到 OverallDeadline（600s）
4. 能力声明 Terminal 且已执行
5. 连续 N 轮无有效工具结果（无进展检测）
6. 宿主取消

## 7. 并行调度（D7-D10）

- 兄弟节点基于同一前线快照（Snapshot N）读取；互不影响；结果按模型调用顺序合并为 Snapshot N+1
- 默认并行；能力 `ParallelSafe=false` 或共享 `AccessesResources` 冲突 → 串行约束
- 非法并行批次 → 整批拒绝，返回结构化错误（D8）
- 本地 mutation：intent → 整批校验 → 原子提交；冲突整批不提交（D9）
- 外部副作用：允许混合执行，接受部分成功，每个调用结果回填 Agent（D10）
- 幂等键：稳定复用（D11）

## 8. 预算（D41）

```csharp
public sealed class DecisionBudgets
{
    public int MaxDecisions { get; init; } = 8;
    public int MaxToolCallsPerDecision { get; init; } = 5;
    public int MaxConcurrentTools { get; init; } = 4;
    public TimeSpan OverallDeadline { get; init; } = TimeSpan.FromSeconds(600);
    public TimeSpan StepIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxToolRetries { get; init; } = 2;
    public int MaxSkillLoadsPerTurn { get; init; } = 3;
}
```

- 按回合重置；会话级仅累计成本监控（D41）
- 多 Agent 并发：预算实例按 Agent 会话隔离，不共享（D41）

## 9. 时钟（D21/D47）

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

- 内核默认实现：`SystemClock`（DateTimeOffset.UtcNow）
- 内核只用 IClock 做超时/重试/deadline，不注入领域时间
- 领域时间上下文由扩展的 ContextContributor 提供（第一版无 time 扩展，时间上下文可省略或由宿主注入当前 UTC）
- 后续 RPG 虚拟时钟：替换 IClock 实现或新增 time 扩展（D47）
- 旧 ITime/虚拟时间代码删除（D47）

## 10. Workflow 运行时（D13）

```csharp
public sealed class WorkflowDefinition
{
    public string Id { get; init; }                 // "world:gift.accept"
    public string Description { get; init; }
    public string StartNodeId { get; init; }
    public int MaxSteps { get; init; } = 16;
    public IReadOnlyList<WorkflowNode> Nodes { get; init; }
    public IReadOnlyList<WorkflowEdge> Edges { get; init; }
}

public interface IWorkflowRunner
{
    Task<WorkflowRunResult> RunAsync(
        WorkflowRunRequest request,
        CancellationToken ct = default);
}
```

- 黑盒：Agent 只调用 `run_workflow(workflowId, input)`，不控制节点（D13）
- 内部确定性执行（节点/guard/副作用）
- 保留旧 flow 的图模型语义（AgentFlowDefinition → WorkflowDefinition），但不再作为普通对话默认入口
