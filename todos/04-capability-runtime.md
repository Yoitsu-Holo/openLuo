# 04 · 能力运行时与决策循环（openLuo.Capabilities）

## 1. 定位

能力运行时负责：

- 能力目录（发现/快照/映射）
- 决策循环（LLM 决策 → 工具调用 → 结果回填）
- 调用分发（按 CapabilityKind 路由到 Provider）
- 策略（并行约束/权限/风险）
- 预算（回合级）
- mutation merge（intent 合并/冲突/原子提交）
- 输出管道（Replyable 分发）
- 幂等
- 执行轨迹（trace）
- Workflow 运行时

不依赖 `openLuo.Llm`；模型决策通过 `ICapabilityDecisionModel` 端口（D18）。

## 2. 能力来源（Provider）

```csharp
public interface ICapabilitySource
{
    string ProviderId { get; }                        // "builtin" / "mcp:image-server" / "world"
    IReadOnlyList<CapabilityDescriptor> ListCapabilities();
}

public interface ICapabilityInvoker
{
    Task<CapabilityResult> InvokeAsync(
        CapabilityCall call,
        CapabilityExecutionContext context,
        CancellationToken ct = default);
}

public sealed class CapabilityExecutionContext
{
    public string GameId { get; init; }               // 宿主元数据（保留但非必需语义）
    public string SessionId { get; init; }
    public string TurnId { get; init; }
    public string SubjectId { get; init; }
    public long SnapshotVersion { get; init; }
    public string InvocationId { get; init; }
    public string IdempotencyKey { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
    public CapabilityPermissions Permissions { get; init; }
    public IReadOnlySnapshot ReadSnapshot { get; init; }        // 前线快照（D7）
    public IMutationCollector MutationCollector { get; init; } // intent 收集
    public IOutputQueue OutputQueue { get; init; }             // Replyable 分发（D3/D4）
}
```

## 3. 能力目录与快照（D14/D29/D30）

```csharp
public interface ICapabilityCatalog
{
    Task<CapabilityCatalogSnapshot> BuildSnapshotAsync(
        CatalogBuildContext context,
        CancellationToken ct = default);
}

public sealed class CapabilityCatalogSnapshot
{
    public long Version { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public IReadOnlyDictionary<string, CapabilityDescriptor> ByCanonicalId { get; init; }
    public IReadOnlyDictionary<string, string> ModelNameToCanonicalId { get; init; }   // D29
    public IReadOnlyDictionary<string, string> CanonicalIdToModelName { get; init; }   // D29
}
```

规则：

- 初始化：从各 ICapabilitySource 收集基础目录并缓存（D14）
- 每轮：根据权限/场景/provider 健康/预算生成不可变快照（D14）
- 只注入摘要（Summary + Usage + InputSchema），不注入完整文档（D30）
- CanonicalId ↔ ModelToolName 双向映射固定在本轮快照内（D29）
- 兄弟节点共享同一快照（D7）

## 4. 决策循环（D2/D17/D18/D41）

```csharp
public sealed class DecisionLoopRequest
{
    public string SessionId { get; init; }
    public string TurnId { get; init; }
    public CapabilityDecisionContext Context { get; init; }   // 最小决策上下文（宿主由 AgentDecisionContext 转换）
    public CapabilityCatalogSnapshot Catalog { get; init; }
    public DecisionBudgets Budgets { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public sealed class DecisionLoopResult
{
    public bool Success { get; init; }
    public string? FinalText { get; init; }
    public IReadOnlyList<OutputItem> Outputs { get; init; }      // 已入队/已发送项
    public IReadOnlyList<AgentToolUseStep> Steps { get; init; }  // 执行轨迹
    public int DecisionsUsed { get; init; }
    public string? TerminationReason { get; init; }
}
```

主循环：

```text
for decision in 1..MaxDecisions:
    if now > deadline: terminate(overall_timeout)
    context = session.Current
    decision = model.DecideAsync(context)          // ICapabilityDecisionModel
    if decision.FinalText 非空 且 Calls 为空:
        return final(FinalText)                     // D2
    if Calls 为空 且 FinalText 为空:
        // 空回复：继续一次受限决策或失败（D2 配套）
        if decisionsUsed >= MaxDecisions: terminate(empty_reply)
        else: continue
    if Calls 非空:
        // 并行调度（D7-D10）
        validated = policy.ValidateBatch(Calls, context)
        if !validated.Ok:
            feed error to model; continue           // D8 整批拒绝
        results = await Dispatcher.ExecuteBatchAsync(
            calls, context, snapshot)               // 前线快照
        context = session.ApplyToolResultsAsync(results)   // Snapshot N+1
```

## 5. 并行调度器（D7-D10）

```csharp
public interface ICapabilityDispatcher
{
    Task<BatchExecutionResult> ExecuteBatchAsync(
        IReadOnlyList<CapabilityCall> calls,
        CapabilityDecisionContext context,
        CapabilityCatalogSnapshot snapshot,
        CancellationToken ct = default);
}
```

规则：

- 兄弟节点基于同一 `ReadSnapshot`（前线快照）；互不观察彼此中间状态（D7）
- 默认并行（≤ MaxConcurrentTools）；`ParallelSafe=false` 或共享资源冲突 → 串行
- 非法并行批次 → 整批拒绝，结构化错误回填（D8）
- 本地 mutation：intent 收集 → 整批校验 → 原子提交；冲突整批不提交（D9）
- 外部副作用：允许混合，部分成功，逐项结果回填（D10）
- 结果按模型调用顺序合并为 Snapshot N+1（D7）
- 每个调用携带幂等键（D11）

## 6. 输出分发（D3/D4）

- 执行中产生 `Replyable/Public` 输出 → 立即 `OutputQueue.EnqueueAsync`（D4）
- Agent 不等待发送完成，继续决策（D4）
- fingerprint 当前 Turn 去重（D3）
- 发送由平台适配层订阅队列完成（D50）

## 7. 策略（并行/权限/风险）

```csharp
public sealed class CapabilityPermissions
{
    public IReadOnlySet<string> AllowedCanonicalIds { get; init; }
    public IReadOnlySet<string> AllowedKinds { get; init; }     // builtin/mcp/workflow/remote_agent
    public bool AllowMutation { get; init; }
    public bool AllowExternal { get; init; }
    public bool AllowDelegation { get; init; }
}

public interface ICapabilityPolicy
{
    BatchValidation ValidateBatch(
        IReadOnlyList<CapabilityCall> calls,
        CapabilityCatalogSnapshot snapshot,
        CapabilityDecisionContext context);
}
```

- 模型只能调用目录内已注册能力（不能发明工具名）
- 参数 schema 校验
- 副作用分级策略（pure/read-only 可自动；mutation 需校验；high risk 需确认）
- 权限由会话/扩展配置注入

## 8. 幂等（D11）

- `CapabilityCall.IdempotencyKey` 稳定复用（同一逻辑调用重试同 key）
- 能力声明 `IdempotencyKind`：
  - `Idempotent`：可安全重试
  - `NonIdempotent`：重试返回风险提示，不自动重试
  - `Unknown`：同 NonIdempotent
- MCP/A2A 适配层负责映射幂等键到协议层（如 header/参数）

## 9. 执行轨迹（trace）

每回合记录：

```text
TurnId
Decision #
ModelToolName → CanonicalId 解析
调用/结果（status, duration, error）
mutation batch 结果
输出项（enqueued/sent/failed）
预算消耗（decisions/time/steps）
终止原因
```

## 10. 回合入口（内核门面）

```csharp
public interface IAgentRuntime
{
    Task<AgentSession> OpenSessionAsync(SessionOpenRequest request, CancellationToken ct = default);
    Task<TurnResult> RunTurnAsync(TurnRequest request, CancellationToken ct = default);
    IAsyncEnumerable<TurnEvent> StreamTurnAsync(TurnRequest request, CancellationToken ct = default);
}
```

- 平台适配层调用 `RunTurnAsync` 或订阅 `StreamTurnAsync`
- 输出经 `IOutputQueue` 即时推送（D50），不等回合结束
