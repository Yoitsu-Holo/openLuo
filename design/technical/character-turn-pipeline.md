# Character Turn Pipeline

本文定义一次玩家对角色行为的标准处理链路。目标是先固定玩法回合协议，再让 `Agent`、`Executor`、`Memory`、`State` 等模块服从这个协议。

## 目标

一次 `CharacterTurn` 表示：

```text
玩家在当前游戏状态下，对一个目标角色发起的一次交互行为。
```

它不等同于一次 LLM 调用，也不等同于一个 Agent step。一个 turn 可以包含记忆检索、规划、工具调用、角色回复、状态结算和记忆写入。

## 标准角色对话链路

```text
memoryRecall
-> plan
-> [toolUse -> flowCheck]*
-> charResp
-> statusUpdate
-> memoryCommit
```

说明：
- `[]*` 表示可重复零次或多次
- 不需要工具时，可以直接从 `plan` 进入 `charResp`
- `charResp` 使用角色沉浸式 system prompt
- `toolUse` 使用工具调用专用 system prompt

## 阶段定义

### memoryRecall

职责：
- 根据玩家输入、目标角色、当前场景、短期对话构建记忆检索 query
- 读取私有记忆、共享记忆、近期事件摘要

输入：
- game id
- character id
- player input
- scene state
- recent conversation
- memory retrieval options

输出：
- memory snippets
- memory summary
- retrieval trace

类型：
- runtime / retrieval

### plan

职责：
- 判断本轮是普通对话、需要工具、需要角色间询问，还是需要澄清
- 输出结构化计划

输入：
- player input
- character context
- memory snippets
- available tools
- current state

输出：
- intent
- next phase
- candidate tools
- planning notes

类型：
- LLM executor 或 deterministic classifier

### toolUse

职责：
- 执行一次或多次工具调用
- 包括外部插件工具、角色间询问工具、宿主内建能力

输入：
- plan
- available tools
- previous tool results
- tool policy

输出：
- tool call
- tool result
- pending confirmation
- execution trace

类型：
- runtime / planner

### flowCheck

职责：
- 判断工具结果是否足够
- 判断是否继续工具调用、进入角色回复、等待用户确认或中止

输入：
- plan
- tool results
- current trace
- pending ability

输出：
- continueToolUse
- readyForResponse
- needUserConfirm
- abort

类型：
- LLM executor 或 deterministic policy

### charResp

职责：
- 生成最终玩家可见的角色自然语言回复
- 维持角色设定、沉浸感和剧情连续性
- 吸收工具结果，但不暴露工具协议

输入：
- character profile
- world context
- scene state
- memory snippets
- recent conversation
- tool result summary
- player input

输出：
- finalText
- tone
- emotion signals
- visible blocks

类型：
- LLM executor

### statusUpdate

职责：
- 根据玩家输入、角色回复、工具结果和当前状态，生成状态变化建议
- 不直接修改状态，只输出 delta

输入：
- current state
- player input
- character response
- tool results
- scene state

输出：
- state deltas
- reason
- confidence

类型：
- LLM evaluator + deterministic apply

### memoryCommit

职责：
- 判断本轮是否值得写入长期记忆
- 生成可存储记忆候选

输入：
- player input
- character response
- state deltas
- tool trace
- current memory policy

输出：
- memory candidates
- emotional weight
- visibility: private/shared

类型：
- LLM evaluator + storage runtime

## Executor 与 Agent 边界

Executor：
- 只执行一个固定任务
- 接收结构化输入
- 返回结构化输出
- 可以直接依赖 `ILlmClient`
- 不拥有角色身份、mailbox、长期状态或全局流程

Agent：
- 拥有身份、状态、记忆和目标
- 通过 turn orchestrator 组合 executor
- 不应把所有推理阶段都塞进一个 system prompt

## 当前落地状态

已新增：
- `Executor` 模块
- `CharacterResponseExecutor`
- `CharacterResponsePromptBuilder`
- `StructuredOutputParser`

尚未接入：
- `Agent`
- `Gameplay`
- `SessionRuntime`

下一步：
- 定义 `CharacterTurn` DTO
- 实现最小 `CharacterTurnOrchestrator`
- 再迁移现有 Agent 对话链路
