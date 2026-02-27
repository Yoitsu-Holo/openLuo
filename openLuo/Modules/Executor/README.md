# Executor Module

`Executor` 定义小型、可组合的任务执行器。

设计约束见：
- [executor-contract-guidelines.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/executor-contract-guidelines.md)

边界：
- Executor 只处理一个明确任务
- 输入必须是结构化 DTO
- 输出可以是结构化 DTO，也可以是纯文本
- LLM 型 executor 直接依赖 `ILlmClient`
- 当前模块不接入 `Agent`、`Gameplay` 或 `SessionRuntime`

当前包含：
- `IExecutor<TInput, TOutput>`：最小执行器接口
- `IExecutorPromptBuilder<TInput>`：把输入 DTO 转为 LLM 消息
- `IStructuredOutputParser`：解析模型返回的 JSON 或 markdown `json` 代码块
- `LlmStructuredExecutor<TInput, TOutput>`：LLM 结构化 executor 基类
- `LlmTextExecutor<TInput>`：LLM 纯文本 executor 基类
- `CharacterResponseExecutor`：角色自然回复生成 executor
- `MemoryRecallExecutor`：语义记忆召回 executor
- `ToolUseExecutor`：角色回合工具调用决策 executor
- `GiftIntentExecutor`：玩家赠礼意图检测 executor，供 chat hook 使用，避免 Agent 直接调用 LLM
- `FlowRoutingExecutor`：Agent flow 候选边语义选择 executor，只能从已通过 guard 的候选边中选择

设计意图：
- `Agent` 负责身份、状态和编排
- `Executor` 负责单个固定任务
- 后续的 turn pipeline 可以组合多个 executor，而不是继续膨胀 Agent 类

示例链路：

```text
CharacterTurnOrchestrator
-> MemoryRecallExecutor
-> PlanExecutor
-> ToolUseExecutor
-> CharacterResponseExecutor
-> StateUpdateExecutor
-> MemoryCommitExecutor
```

当前已实现：
- `MemoryRecallExecutor`
  - 输入：游戏 id、角色 id、当前玩家输入、场景、近期对话、召回选项
  - 输出：`query`、`memorySnippets`、`memorySummary`、`retrievalTrace`
  - 当前默认实现走规则化 query projector，可由新的 memory recall service 适配真实 RAG
- `CharacterResponseExecutor`
  - 输入：角色设定、世界观、场景、记忆、工具结果、对话历史、玩家输入
  - 输出：玩家可见的角色自然语言回复文本
- `ToolUseExecutor`
  - 输入：角色设定、计划摘要、工具目录、对话历史、玩家输入
  - 输出：是否调用工具、工具名、参数、选项，以及可选决策原因
- `PlanExecutor`
  - 输入：角色设定、世界观、场景、记忆摘要、状态摘要、可用工具、对话历史、玩家输入
  - 输出：是否需要工具、候选工具列表
- `FlowRoutingExecutor`
  - 输入：flow id、当前节点、上一节点输出、flow 状态摘要、候选边列表
  - 输出：`selectedEdgeId`、`selectedNodeId`、`confidence`、`reason`、`stopReason`
  - 注意：它不执行节点、不修改状态、不调用工具，只为 Agent flow runner 提供受约束的路由建议
- `StateUpdateExecutor`
  - 输入：当前状态、场景、玩家输入、角色回复、工具结果
  - 输出：结构化 `StateDelta[]`
- `GiftIntentExecutor`
  - 输入：目标角色名、玩家输入、背包物品快照
  - 输出：是否存在赠礼意图、候选物品引用、置信度、原因
  - 注意：它只做意图检测；是否真的赠送仍必须由后续工具能力执行确认
- `CharacterTurnOrchestrator`
  - 仅在 Executor 模块内部做最小组合
  - 当前串联：`memoryRecall -> plan -> charResp? -> statusUpdate?`
  - 注意：Agent 主链路目前直接编排各 executor stage，不通过该 demo orchestrator

说明：
- 当前 Agent 主链路已经接入 plan / tool-use / response / state-update executor
- Memory recall / write 由 Agent gateway 与 Memory 模块直接协作
