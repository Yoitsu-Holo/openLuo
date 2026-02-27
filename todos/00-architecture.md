# openLuo 架构重写设计 · 总览

> 状态：已确认（2026-08-13 访谈收敛）
> 定位：demo / preview，接受一切破坏性变更；一次性迁移，不保留旧逻辑与旧数据兼容。

## 1. 目标

把 openLuo 从"游戏特化 Agent + 固定流程执行器"重构为：

> **通用 Agent 内核 + 领域扩展**

- Agent 内核 = 会话运行时 + 上下文组装 + 能力发现/调度 + 决策循环 + 输出管道 + 扩展宿主
- 内核不知道自己是 RPG、桌宠还是聊天陪伴；它变成什么样完全取决于加载的扩展
- Agent 自主决策：不再固定 `memory → plan → exec → state_update`，而是根据工具自行组合
- 内核零业务配置硬编码；领域数据随扩展走

## 2. 核心原则（决策记录摘要）

| # | 决策 |
|---|------|
| D1 | 一次性迁移，删除旧逻辑与旧数据兼容 |
| D2 | 无 tool_calls 的非空文本 = 最终回复并结束 |
| D3 | Replyable = 可消费输出队列；仅当工具执行带可回复标签才直接回复，否则为静默后台流程 |
| D4 | Replyable 立即进入外层输出管道并发送；Agent 调度与发送并行 |
| D5 | 发送失败：有限重试 → 放弃该条 + 固定失败消息 → 继续下一条 |
| D6 | 输出队列仅内存实现（进程重启丢失可接受） |
| D7 | 并行：默认并行，能力声明串行约束；兄弟节点基于同一前线快照，互不影响 |
| D8 | 非法并行整批拒绝，返回结构化错误给 Agent 重新规划 |
| D9 | 并行 mutation：基于同一快照生成 intent → 整批校验 → 原子提交；冲突整批不提交 |
| D10 | 外部副作用（MCP/远程/发消息）允许与本地 mutation 混合，接受部分成功，结果回填 Agent |
| D11 | 每次工具调用携带稳定幂等键；能力声明幂等性；不可靠幂等工具重试时返回风险提示 |
| D12 | Skill = 上下文指导内容，非可调用函数；摘要注入目录，完整内容按需 `core:load_skill` |
| D13 | Workflow = 黑盒可调用能力；Agent 只调不控制内部节点 |
| D14 | 工具目录：初始化加载基础注册，每轮构造不可变快照 |
| D15 | ContextManager 按 Agent 会话隔离（GameId/ConversationId/AgentId 维度隔离） |
| D16 | ContextManager 输出结构化快照；LLM 消息由适配层最后一层转换 |
| D17 | LLM 同时返回文本+tool_calls：文本为内部过程文本，不进公共输出 |
| D18 | 决策循环 = 通用无角色内核（openLuo.Capabilities）；角色/领域适配层在宿主/扩展 |
| D19 | 能力统一元数据 CapabilityDescriptor（已确认字段集，见 §4） |
| D20 | 内核状态机制：通用 StateSnapshot + MutationIntent + 原子提交；字段校验由扩展 handler 承担 |
| D21 | 内核仅 wall-clock（UTC，用于超时/重试/deadline）；领域时间由扩展提供 |
| D22 | 扩展：进程内 C# 为主，外部进程经 MCP/A2A/JSON-RPC 接入 |
| D23 | 扩展 = 独立程序集 + extension.jsonc manifest + 自动发现；目录即信任边界 |
| D24 | 扩展目录名以 `.disable` 结尾 → 完全跳过不加载 |
| D25 | 扩展生命周期：宿主启动扫描一次，不热加载 |
| D26 | 扩展入口：manifest 声明 assembly + entryType（IAgentExtension.Configure） |
| D27 | 扩展依赖：manifest requires（minVersion）；依赖失败只禁用该扩展 |
| D28 | 扩展注册项自动命名空间化：`<extension-id>:<local-id>`；`core:` 保留给内核 |
| D29 | CanonicalId 与 ModelToolName 分离；每轮快照固定双向映射 |
| D30 | 能力只注入摘要（summary + usage + schema），不注入完整文档 |
| D31 | Skill 完整内容：会话缓存 + 相关性动态淘汰（按预算） |
| D32 | Memory：基线自动（ContextContributor）+ 深度按需（memory:search 能力） |
| D33 | 内核保留 core 能力：load_skill/unload_skill/list_loaded_skills/inspect_capabilities/delegate_agent/list_mcp_servers/list_mcp_tools |
| D34 | MCP 用官方 ModelContextProtocol SDK（2.1.0）；PluginRuntime 整体废弃重写 |
| D35 | 对话存储：内核定义 IConversationStore 端口，宿主提供 SQLite 实现 |
| D36 | LLM 决策模型适配层 = openLuo.Capabilities.Llm（依赖 Capabilities + Llm） |
| D37 | 平台适配层拆独立工程：Cli/Tui/Gui/Qqbot；openLuo 只剩入口+组合根 |
| D38 | 测试按工程拆分，E2E 独立项目 |
| D39 | 第一版硬验收：CLI 端到端可玩（除多媒体内容），TUI/GUI/QQbot 后续修复 |
| D40 | 领域扩展第一版 5 个：memory / companion / world / party / media（无 time） |
| D41 | 回合预算：MaxDecisions=8, MaxToolCallsPerDecision=5, MaxConcurrentTools=4, OverallDeadline=600s, StepIdleTimeout=30s, MaxToolRetries=2, MaxSkillLoadsPerTurn=3；按回合重置，会话级仅累计成本监控；多 Agent 并发预算隔离 |
| D42 | ContextContributor = 每轮快照的内容贡献入口；原子独立、只读查询、失败独立降级（Unavailable 结构化） |
| D43 | EnhanceMsg → ContextRegion/ContextContribution（全局上下文）；EnhanceChat → 通用标签渲染器注册 + 白名单 + 输出剥离（消息级，正交） |
| D44 | 扩展依赖注入：A 声明依赖 B，Host 解析注入；Agent 不感知 A→B |
| D45 | 角色归属：companion 拥有人格身份；world 按 SubjectId 存状态；party 依赖 companion |
| D46 | 第一版扩展 5 个的依赖：world→(无 time 依赖，直接用内核 IClock)，companion→memory，party→companion，media→无 |
| D47 | 时间：内核 IClock 抽象（默认 UTC），旧 ITime/虚拟时间代码删除；后续 RPG 虚拟时钟再引入 time 扩展 |
| D48 | MCP server 配置：宿主级 config/mcp-servers.jsonc，openLuo.Capabilities.Mcp 启动时连接 |
| D49 | Playground 保留并全量重写为新架构 demo，承担部分 E2E；拼写规范为 openLuo.Playground |
| D50 | 平台收到可回复内容立即推送+回复，不等待回合结束 |

## 3. 目标工程拓扑

```text
openLuo.Foundation              基础（Block/Logger/DB 工厂等，无依赖）
openLuo.Llm                     LLM 客户端（→ Foundation）
openLuo.AgentContext            上下文（→ Foundation + Capabilities；结构化快照/Contributor/Assembler/Session）
openLuo.Capabilities            能力内核（→ Foundation；目录/调度/决策循环/策略/预算/merge/Workflow 运行时）
openLuo.Capabilities.Llm        LLM 决策模型桥接（→ Capabilities + Llm）
openLuo.Capabilities.Mcp        MCP 适配（→ Capabilities + ModelContextProtocol）
openLuo.Cli / Tui / Gui / Qqbot 平台适配（→ AgentContext/Capabilities 契约 + 宿主端口）
openLuo                         入口 + 组合根（→ 全部）
openLuo.Playground              新架构 demo（→ 相关工程）

extensions/
  memory/    openLuo.Extension.Memory
  companion/ openLuo.Extension.Companion
  world/     openLuo.Extension.World
  party/     openLuo.Extension.Party
  media/     openLuo.Extension.Media
```

依赖拓扑无环；允许 A→B 多级依赖，禁止循环。

## 4. 核心契约速览

### CapabilityDescriptor（D19）

```csharp
public sealed class CapabilityDescriptor
{
    string CanonicalId;          // "world:inventory.read"
    string ModelToolName;        // 当前 Turn 映射名
    string DisplayName;
    string Summary;              // 摘要（注入上下文）
    string Usage;                // 何时使用
    CapabilityKind Kind;         // builtin | mcp | workflow | remote_agent
    string ProviderId;
    string Version;
    SideEffectClass SideEffect;  // pure | read_only | external | mutation | delegation | terminal
    CompletionPolicy Completion; // continue | may_complete | terminal
    OutputVisibility Visibility; // silent | replyable | public
    bool ParallelSafe;
    object InputSchema;          // JSON Schema
    IReadOnlyList<string> Aliases;
    RiskLevel Risk;              // low | medium | high
    bool RequiresConfirmation;
    IdempotencyKind Idempotency; // idempotent | non_idempotent | unknown
    IReadOnlyList<string> AccessesResources;
}
```

### 回合流程（D2/D3/D4/D17/D41）

```text
TurnRequest
  → ContextAssembler 构建 AgentDecisionContext 快照（Contributor 串行贡献）
  → CapabilityDecisionLoop：
      LLM 决策（ICapabilityDecisionModel）
      → 无 tool_call 非空文本 → 最终回复，结束
      → 有 tool_call：
          并行/串行执行（前线快照）
          非法并行 → 整批拒绝，结构化错误
          Replyable → 立即入输出管道发送
          MutationIntent → 整批校验原子提交
          结果回填 → 继续决策
  → TurnResult（最终回复 + 输出项 + 轨迹 + 状态版本）
```

### 预算（D41）

| 预算 | 默认 | 作用域 |
|---|---|---|
| MaxDecisions | 8 | 单回合 |
| MaxToolCallsPerDecision | 5 | 单轮决策 |
| MaxConcurrentTools | 4 | 单批 |
| OverallDeadline | 600s | 单回合 |
| StepIdleTimeout | 30s | 单步 |
| MaxToolRetries | 2 | 单工具 |
| MaxSkillLoadsPerTurn | 3 | 单回合 |

预算按回合重置；会话级仅累计成本监控；多 Agent 并发预算隔离（D41）。

## 5. 输出管道（D3-D6）

```text
Replyable 输出项 → 会话级内存输出队列（Sequence 单调递增）
平台适配层订阅 → 收到即推送+回复（D50）
按会话/频道顺序发送：后一条等待前一条完成
失败：有限重试退避 → 放弃 + 固定失败消息 → 继续下一条
```

## 6. 后续文档导航

1. `01-project-layout.md` — 工程拆分与目录结构
2. `02-kernel-contracts.md` — 内核契约细节
3. `03-agent-context.md` — 上下文系统
4. `04-capability-runtime.md` — 能力运行时与决策循环
5. `05-extension-system.md` — 扩展宿主
6. `06-mcp-integration.md` — MCP 集成
7. `07-domain-extensions.md` — 5 个领域扩展
8. `08-platform-adapters.md` — CLI/TUI/GUI/QQbot/Playground
9. `09-migration.md` — 删除清单与迁移
10. `10-testing.md` — 测试策略
11. `11-implementation-steps.md` — 实施步骤
