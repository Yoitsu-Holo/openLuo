# 03 · 上下文系统（openLuo.AgentContext）

## 1. 定位

ContextManager 负责"每轮决策上下文快照"的组装与生命周期，按 Agent 会话隔离（D15）。
它不执行工具、不修改状态、不发送输出——只提供"上下文事实"。

## 2. 核心类型

```csharp
// 每轮快照构造请求（Contributor 唯一输入，不可变）
public sealed class ContextBuildRequest
{
    public string SessionId { get; init; }
    public string SubjectId { get; init; }          // 角色/玩家/世界主体
    public string TurnId { get; init; }
    public string? UserInput { get; init; }
    public IReadOnlyList<object>? UserBlocks { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
}

// 单条上下文贡献（Contributor 的产出，只读）
public sealed class ContextContribution
{
    public string Id { get; init; }
    public string ContributorId { get; init; }
    public ContextRegion Region { get; init; }
    public string Content { get; init; }
    public int Priority { get; init; }
    public long TokenEstimate { get; init; }
    public string? Version { get; init; }
}

// 贡献状态（失败降级用，D42）
public enum ContextSourceStatus { Ok, Unavailable }

public sealed class ContextSourceState
{
    public string SourceId { get; init; }
    public ContextSourceStatus Status { get; init; }
    public string? Reason { get; init; }             // timeout / error / rejected
    public bool Retryable { get; init; }
}

// 结构化决策上下文（内核/LLM 适配层的输入）
public sealed class AgentDecisionContext
{
    public string SessionId { get; init; }
    public string TurnId { get; init; }
    public long SnapshotVersion { get; init; }
    public IReadOnlyList<ContextContribution> Contributions { get; init; }
    public IReadOnlyList<ContextSourceState> SourceStates { get; init; }
    public IReadOnlyList<ContextMessage> Conversation { get; init; }
    public IReadOnlyList<CapabilitySummary> Capabilities { get; init; }   // 摘要注入（D30）
    public IReadOnlyList<SkillSummary> Skills { get; init; }              // 摘要注入（D12）
    public IReadOnlyList<WorkflowSummary> Workflows { get; init; }
    public IReadOnlyList<RemoteAgentSummary> RemoteAgents { get; init; }
    public DecisionBudgets Budgets { get; init; }
}
```

## 3. ContextContributor（D42）

```csharp
public interface IContextContributor
{
    string Id { get; }                                    // "memory" / "world:state" ...

    Task<ContextContributionResult> ContributeAsync(
        ContextBuildRequest request,
        CancellationToken ct = default);
}

public sealed class ContextContributionResult
{
    public ContextSourceState State { get; init; }
    public IReadOnlyList<ContextContribution> Contributions { get; init; }
}
```

规则（D42/D43）：

- 原子独立：Contributor 之间禁止依赖，不读其他 Contributor 结果
- 只读查询：允许查询 memory/状态/历史，禁止 mutation、禁止输出、禁止委托
- 串行执行：按确定性顺序逐个调用（顺序 = 注册顺序 + 优先级）
- 失败独立降级：单个 Contributor 失败 → 返回 `Unavailable` 结构化状态，不阻塞其他贡献
- 只有身份/权限/会话标识等核心来源失败才终止本轮

## 4. ContextAssembler

```csharp
public interface IContextAssembler
{
    Task<AgentDecisionContext> BuildAsync(
        ContextBuildRequest request,
        CancellationToken ct = default);
}
```

职责：

- 串行调用所有 Contributor（D42）
- 合并贡献、按 Region/Priority 排序
- 去重、token/字符预算裁剪
- 合并 SourceStates（Unavailable 保留结构化状态）
- 生成不可变快照（SnapshotVersion 递增）
- 不执行工具、不修改状态

## 5. 上下文会话

```csharp
public interface IAgentContextSession
{
    string SessionId { get; }
    AgentDecisionContext Current { get; }

    Task<AgentDecisionContext> CreateTurnSnapshotAsync(
        ContextBuildRequest request,
        CancellationToken ct = default);

    Task<AgentDecisionContext> ApplyToolResultsAsync(
        IReadOnlyList<CapabilityResult> results,
        CancellationToken ct = default);

    Task CommitTurnAsync(
        TurnCompletion completion,
        CancellationToken ct = default);
}
```

- 每会话独立实例（D15）
- 快照不可变；工具结果通过 `ApplyToolResultsAsync` 生成新快照（前线快照模型，D7）
- 维护：当前会话历史视图、已加载 Skill 完整内容（会话缓存，D31）、pending 输出状态、预算

## 6. 对话存储（D35）

```csharp
public interface IConversationStore
{
    Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(
        string sessionId, int limit, CancellationToken ct = default);

    Task AppendAsync(ConversationTurn turn, CancellationToken ct = default);
}

public sealed class ConversationTurn
{
    public string SessionId { get; init; }
    public string TurnId { get; init; }
    public string SpeakerId { get; init; }
    public string SpeakerRole { get; init; }      // user | agent | system
    public string Content { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public IReadOnlyList<object>? Blocks { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();  // EnhanceChat 存储层（D43）
}
```

- 内核只定义端口；宿主提供 SQLite 实现（D35）
- Metadata 永不进入正文；渲染由标签渲染器在序列化点完成（D43）

## 7. 消息级标签（EnhanceChat，D43）

```csharp
public interface IMessageTagRenderer
{
    string Key { get; }                              // 元数据键，如 "type"
    IReadOnlyList<string> Render(Dictionary<string, string> metadata);
}

public sealed class MessageTagPipeline
{
    // 内核提供白名单 + 渲染器注册 + 输出剥离
    void Register(IMessageTagRenderer renderer);
    IReadOnlyList<string> Render(Dictionary<string, string>? metadata);
    string Strip(string content);                    // 输出侧剥离 [NAME: value] 标记
}
```

- 内核保证 `[NAME: value]` 语义单标签协议 + 白名单 + 输出剥离（D43）
- 扩展注册自己的标签渲染器（`[TYPE: card]`、`[LOCATION: ...]` 等）
- 与全局上下文（EnhanceMsg → ContextContribution）正交（D43）

## 8. 上下文区域（EnhanceMsg 映射）

```csharp
public enum ContextRegion
{
    Identity,          // companion: 角色人格
    TimeContext,       // 内核 IClock 或未来 time 扩展
    WorldContext,      // 扩展贡献的世界信息
    SceneState,        // 当前场景/状态摘要
    GoalContext,       // 当前目标/任务
    LongTermMemory,    // memory 扩展基线记忆
    RuntimeRules,      // 各扩展按需贡献的规则
    ConversationHistory,
    CurrentUserInput,
    ToolResults,
}
```

- 扩展的 Contributor 按 Region 贡献内容（D43）
- 内核只做合并/排序/裁剪，不感知字段含义（D20 一致）

## 9. 失败降级语义（D42）

```text
memory Contributor 查询超时
  → SourceStates: [{ sourceId: "memory", status: "unavailable", reason: "timeout", retryable: true }]
  → 不注入伪造记忆
  → 其他 Contributor 正常
  → Agent 看到"记忆不可用"，可自主决定替代方案
```

## 10. Skill 加载（D12/D31/D33）

```csharp
public interface ISkillService
{
    Task<SkillSummary?> GetSummaryAsync(string skillId, CancellationToken ct = default);
    Task<SkillDocument?> LoadFullAsync(string skillId, CancellationToken ct = default);  // core:load_skill
    Task UnloadAsync(string skillId, CancellationToken ct = default);                    // core:unload_skill
    IReadOnlyList<string> ListLoaded(CancellationToken ct = default);                    // core:list_loaded_skills
}
```

- 摘要注入每轮目录（D30）
- 完整内容：会话缓存 + 按相关性动态淘汰（超预算时淘汰最不相关，D31）
- 淘汰基于当前输入相关性评估 + 预算（MaxSkillLoadsPerTurn 与 token 预算）
