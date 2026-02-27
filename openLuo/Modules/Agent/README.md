# Agent Module

`Modules/Agent` 负责角色 Agent 的运行时、消息分发接入、角色回合 flow 执行、Agent 间协作入口，以及角色专属上下文与状态落地。

它不是游戏主循环，也不是 LLM 直接调用层。Agent 只负责编排角色回合，并把具体推理交给 `Modules/Executor`，把副作用交给能力层和宿主。

## 目录职责

- `Application/Agents/Character/`
  角色 Agent 主体与角色回合入口。
- `Application/Runtime/`
  runtime、dispatcher、mailbox、消息模型、执行上下文。
- `Application/Chat/`
  玩家 chat 入口、chat turn hook、统一 chat pipeline。
- `Application/Orchestration/`
  多角色协作、编排与调度相关逻辑。
- `Application/Profiles/`
  角色画像与 profile 适配。
- `Application/Context/`
  Agent 上下文与会话态存储。
- `Core/Interfaces/Character`
  角色 Agent 专属核心接口。
- `Core/Interfaces/Flow`
  flow 注册、运行、节点执行、guard 评估接口。
- `Core/Models/Character`
  角色 turn / prompt / state / context 相关模型。
- `Core/Models/Execution`
  Agent 能力执行与 pending ability 模型。
- `Core/Models/Flow`
  flow、node、edge、guard、run request/result 模型。
- `Core/Models/Runtime`
  runtime invocation、multi-character command、party task 模型。
- `Infrastructure/`
  Agent 上下文与记忆相关基础设施适配。

## 边界

- Agent 不直接拥有游戏主循环。
- Agent 不直接调用 `ILlmClient`；所有结构化推理必须经过 `Modules/Executor`。
- Agent 不直接越过 runtime 去调用别的角色底层实现。
- 插件和扩展只能通过 flow、能力、profile、port 等受控入口接入。

## 当前整体结构

当前角色主链已经是 flow 驱动，不再依赖旧的硬编码 turn 分支。

```text
GameSessionRuntime / CLI
-> GameEngine
-> AgentInvocationRouter
-> PlayerChatDispatcher
-> AgentRuntimeHub
-> AgentDispatcher
-> DefaultAgentMessageHandler
-> CharacterAgent
-> AgentFlowRunner
-> built-in flow
-> Character*FlowNodeExecutor
-> Character*Node / Executor
```

说明：

- `CharacterTurn` 仍然是概念，不是独立顶层模块。
- `CharacterAgent` 是稳定对外入口。
- `AgentFlowRunner` 是一轮角色回合的通用执行器。
- 旧 `Application/Reasoning` 链路已经移除，不再保留 `AgentReasoner` 兼容入口。

## Flow 模型

Agent 的扩展点以可注册 flow 为中心，而不是继续往 `CharacterAgent` 里堆硬编码分支。

核心模型位于 `Core/Models/Flow`：

- `AgentFlowDefinition`
  完整 flow 定义，包含 start node、nodes、edges、max steps。
- `AgentFlowRegistration`
  对外最小注册模型，节点只声明 `id + callName`，边只声明 `fromNodeId + toNodeId + when`。
- `AgentFlowNode`
  单个执行节点，可指向 executor、capability、memory、state、terminal。
- `AgentFlowEdge`
  flow 有向边，包含自然语言 `when` 与程序性 guards。
- `AgentFlowGuard`
  确定性硬约束，不能交给 LLM 自由绕过。
- `AgentFlowRunRequest` / `AgentFlowRunResult`
  flow runner 标准输入输出。

运行策略：

```text
AgentFlowRunner
-> execute current node
-> collect outgoing edges
-> guards filter impossible edges
-> FlowRoutingExecutor selects one candidate when needed
-> continue until terminal node or max step limit
```

重要边界：

- Agent 负责 flow 注册、trace、loop limit、权限边界、副作用边界。
- `Executor.FlowRoutingExecutor` 只负责在合法候选边中做语义选择。
- LLM 路由结果只能选择已允许的 edge，不能发明新 node，也不能绕过 guard。
- 外部注册方不需要提供 node kind、output key、edge id；这些由 Agent 内部生成或推导。

## Built-in Flows

### `character.standard_chat`

当前主角色回合 flow：

```text
memoryRecall
-> plan
-> plannedExecution
-> stateUpdate
-> done
```

对应实现见：

- `Application/Flow/CharacterStandardChatFlow.cs`
- `Application/Flow/Nodes/CharacterPlannedExecutionNode.cs`

用途：

- 读取长期记忆
- 生成本轮计划
- 在局部复合执行器里完成回复/工具调用
- 最后做状态结算并产出 `CharacterTurnResult`

### `character.agent_ask`

当前角色间内部咨询专用轻量 flow：

```text
memoryRecall
-> plan
-> plannedExecution
-> finalize
-> done
```

对应实现见：

- `Application/Flow/CharacterAgentAskFlow.cs`
- `Application/Flow/Executors/CharacterFinalizeReplyFlowNodeExecutor.cs`

用途：

- 用于 `AgentAsk` / `ask_character`
- 保留角色内计划与回复能力
- 明确跳过 `state_update`
- 直接把轻量内部答复收束成 `CharacterTurnResult`

`CharacterAgent` 会按消息类型自动切 flow：

- `AgentMessageType.AgentAsk` -> `character.agent_ask`
- 其他普通角色对话 -> `character.standard_chat`

## Planned Execution

`planned_execution` 已经接入主角色 flow，不再只是概念。

它是一个复合执行节点，当前职责是：

1. 根据 `plan` 构造局部 step plan
2. 执行一个或多个 `response`
3. 执行一个或多个 `tool_use`
4. 以 `response` 收束最终玩家可见回复

内部 step 计划由两层组成：

- 主路径：`PlannedExecutionPlanExecutor`
- 降级路径：`DefaultCharacterExecutionPlanBuilder`

step 当前支持的元数据：

- `stepId`
- `kind`
- `goal`
- `allowedToolNames`
- `maxIterations`
- `responseMode`
- `completionHint`

当前允许的 step kind：

- `response`
- `tool_use`

当前 planner 约束：

- 最多 5 步
- 最后一步通常以 `response` 收束
- 工具步骤可通过 `maxIterations` 在局部循环内多轮执行

说明：

- 当前实现已经收敛到：
  - `response`
  - `tool_use`
- `response` 的时机语义由 `responseMode` 承载，例如：
  - `ack_before_action`
  - `report_after_action`
  - `final_direct_answer`
- 设计背景与迁移理由见：
  [planned-execution-schema-convergence.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/planned-execution-schema-convergence.md)

## Tool Use Loop

`tool_use` 已经不是单次工具判定，而是局部 loop step。

当前工具执行请求会携带：

- `AllowedToolNames`
- `LastToolResult`
- `Iteration`

这使得 `planned_execution` 可以在单个 step 内做受控的：

```text
choose tool
-> execute
-> merge result
-> continue or stop
```

## 多角色调用链路

角色 A 询问角色 B 时，不能绕过 runtime 直接调用 B 的底层 LLM。当前标准链路是：

```text
Player
-> PlayerChatDispatcher
-> AgentRuntimeHub.RequestAsync(A)
-> AgentDispatcher
-> CharacterAgent(A)
-> character.standard_chat
-> planned_execution
-> ask_character
-> InterAgentMessenger.AskAsync
-> AgentRuntimeHub.RequestAsync(B, AgentAsk)
-> CharacterAgent(B)
-> character.agent_ask
-> AgentReply
-> InterAgentMessenger 转回 ask_character 工具结果
-> CharacterResponseNode(A) 基于工具结果生成最终回复
-> CharacterStateUpdateNode(A)
```

这条链说明两件事：

- 角色间调用也是 runtime 级消息，不是内部函数直连。
- `ask_character` 走轻量内部咨询 flow，而不是完整玩家回合 flow。

## 执行上下文与超时管理

当前多 Agent 协作已经引入统一执行上下文：

- `Application/Runtime/AgentExecutionContext.cs`

它承载：

- `ConversationId`
- `StartedAtUtc`
- `OverallDeadlineUtc`
- `StepIdleTimeout`
- `LastProgressAtUtc`
- `LastProgressReason`

语义不是“每层自己单独计时”，而是：

- 一轮对话共享一个全局 `overall timeout`
- 每个子步骤共享一个 `idle timeout`
- 只要执行链持续上报 progress，就刷新 idle 计时

当前已经接入这套执行上下文的链路包括：

- `PlayerChatDispatcher`
- `AgentRuntimeHub`
- `AgentDispatcher`
- `CharacterAgent`
- `AgentFlowRunner`
- `InterAgentMessenger`
- `AgentCapabilityContext`

这套模型的目标是让复杂递归调用、多角色协作、子图执行都服从同一套超时语义，而不是由每一层各自做独立硬裁决。

## Subgraph Executor

当前已经提供通用子图执行器：

- `Application/Flow/Executors/SubgraphFlowNodeExecutor.cs`

调用约定：

```json
{
  "id": "run-child-flow",
  "callName": "flow.subgraph",
  "inputMap": {
    "flowId": "demo.child",
    "inheritKeys": "turnContext,plan",
    "exportOutputKey": "finalReply",
    "maxStepsOverride": "8"
  }
}
```

支持字段：

- `flowId`
  子图 id。
- `inheritKeys`
  从父 flow state 继承指定键。
- `inheritAllInputs=true`
  直接继承全部父 state。
- `exportOutputKey`
  把子图输出中的某个 key 回写到父 state。
- `maxStepsOverride`
  子图局部步数上限。

当前状态：

- 子图执行器代码已经可用
- playground 有独立 demo
- 但还没有接入 `character.standard_chat`

## Chat 架构约束

### 单一玩家入口

- 玩家 `/chat` 必须先进入统一 chat pipeline。
- 不允许“插件聊天链”和“agent 聊天链”并列成为等价主入口。
- 当前约束是：`player -> targetCharacter` 通过统一 dispatch 进入目标角色 runtime。

### Dispatch 原则

- 玩家输入和 agent-to-agent 通信都可以复用 dispatch / mailbox / runtime。
- 玩家侧必须在入口层显式确定目标角色，不能把目标解析继续下沉到二次编排器。
- `MultiCharacterOrchestrator` 负责多角色协作，不负责普通单聊主入口。

### Hook 分层

- `OnChatTurnBefore` / `OnChatTurnAfter`
  处理玩家这一轮输入与角色这一轮输出的前后置逻辑。
- `OnAgentStepBefore` / `OnAgentStepAfter`
  处理 Agent 内部一次推理或能力执行轮次。

不要把 chat-turn 级副作用挂到 agent-step hook 上，否则多步 tool-use 会导致重复结算。

### 输出分层

- internal planning、trace、决策理由默认不直接暴露给玩家。
- 玩家可见输出只应包含最终角色回复和宿主明确允许展示的可见块。
- trace 只在调试模式或显式 trace 开关下暴露。

### 副作用原则

- 角色对白不能宣告一个宿主尚未真正执行成功的副作用。
- 例如“我已经收下礼物”只能在对应能力确实成功执行后出现。
- 如果能力失败、库存不足、礼物不存在，角色只能做条件式或拒绝式回应，不能制造已完成事实。

### Prompt 策略

- 不同阶段可以拥有不同 system prompt。
- 角色对白阶段使用角色扮演 prompt。
- 工具调用、状态结算、flow 路由、局部计划等阶段使用各自专用 prompt。
- 角色定义、世界观、目标、场景、记忆等信息以增强上下文块注入，而不是为每个角色维护完全分叉的系统提示词。

## 当前工程判断

当前 `Modules/Agent` 已经进入“结构基本成立，后续以行为质量调优为主”的阶段。

已经落地的核心能力：

- flow-driven character agent
- lightweight internal ask flow
- planned execution composite node
- tool-use local loop
- unified execution context
- subgraph executor skeleton

后续演进重点更偏向：

- planner 质量
- 角色回复与记忆事实一致性
- 状态传播语义
- 可观察性与 trace 打磨
