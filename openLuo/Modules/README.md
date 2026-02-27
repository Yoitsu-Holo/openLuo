# Modules Overview

`openLuo/Modules` 是当前项目的功能模块目录。

当前拆分原则不是“纯粹按技术分层”，而是“按稳定子域划分”，目标是让每个模块有明确职责、可控影响范围和清晰依赖方向。

当前模块数量本身不是问题。真正需要持续治理的是：

- 是否存在重复主链
- 是否存在跨层回穿
- 是否有多个模块同时承担同一类推理职责

当前阶段的目标不是继续加模块，而是把已有模块收敛成更稳定的四层结构。

## 理想分层

### 第 1 层：基础能力层

- `Llm`
- `Embedding`
- `Memory`
- `WorldState`
- `Assets`
- `Content`
- `Commanding`

职责：
- 提供稳定能力
- 不拥有角色回合编排
- 不承载玩法流程

### 第 2 层：推理执行层

- `Executor`

职责：
- 承载固定任务的结构化执行
- 输入必须是结构化 DTO
- 输出是最小公开结果，可以是结构化 DTO，也可以是纯文本
- 直接依赖 `Llm` / `Memory` / `WorldState` 等基础能力模块

设计约束：
- executor 间只传公开成立的任务结果，不传仅供下游理解的内部控制状态
- 详细规则见 [executor-contract-guidelines.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/executor-contract-guidelines.md)

### 第 3 层：Agent 运行层

- `Agent`
- `AgentCapabilities`
- `InterAgent`

职责：
- 管理 Agent 身份、runtime、mailbox、能力视图与多 Agent 协作
- 不应继续膨胀成新的底层推理能力层
- 应优先编排 `Executor`，而不是重复发明新的结构化推理协议

### 第 4 层：宿主与入口层

- `Gameplay`
- `SessionRuntime`
- `PluginRuntime`
- `GameBridge`
- `AppShell`

职责：
- 负责宿主编排、会话入口、插件入口、桥接与装配
- 不应直接承担底层模型协议兼容

## 理想依赖关系图

```text
AppShell
  -> SessionRuntime
  -> Gameplay
  -> PluginRuntime
  -> GameBridge
  -> Agent
  -> Executor
  -> Llm / Embedding / Memory / WorldState / Assets / Content / Commanding

SessionRuntime
  -> Gameplay
  -> Agent

GameBridge
  -> Gameplay
  -> Agent
  -> WorldState
  -> Assets
  -> PluginRuntime (interfaces)

Gameplay
  -> Executor
  -> Agent
  -> WorldState
  -> Content
  -> Commanding
  -> Assets

Agent
  -> Executor
  -> AgentCapabilities
  -> InterAgent
  -> Memory
  -> Content

多角色对话必须经过 `AgentRuntimeHub / AgentDispatcher`：

```text
PlayerAction
  -> Gameplay/GameEngine resolves active character
  -> Agent/PlayerChatDispatcher
  -> AgentRuntimeHub(A)
  -> CharacterAgent(A)
  -> Executor.ToolUse chooses ask_character
  -> AgentCapabilities
  -> InterAgent
  -> AgentRuntimeHub(B)
  -> CharacterAgent(B)
  -> InterAgent result
  -> CharacterAgent(A) final response
```

Executor
  -> Llm
  -> Memory
  -> WorldState
  -> Embedding

Memory
  -> Embedding
  -> Database

Llm / Embedding / WorldState / Assets / Content / Commanding
  -> no upper business dependency
```

核心原则：

- `Gameplay` / `Agent` 负责编排
- `Executor` 负责单任务推理
- `Llm` / `Embedding` / `Memory` / `WorldState` 负责基础能力
- `GameBridge` / `SessionRuntime` / `PluginRuntime` 只做入口与桥接

## 当前结构判断

当前已经相对清晰的模块：

- `Llm`
- `Embedding`
- `Memory`
- `Executor`
- `WorldState`
- `Content`
- `Assets`
- `Commanding`

当前仍需继续重构的模块：

- `Gameplay`
- `Agent`
- `GameBridge`
- 根级 `Core` / `Infrastructure` 对模块的泄漏依赖

其中最需要优先收口的是“重复主链”，不是目录命名。

## 模块列表

### `AppShell`

职责：
- 宿主应用装配
- 配置读取
- DI 注册与启动前准备

应依赖：
- 所有业务模块

不应承载：
- 玩法逻辑
- 内容定义
- 插件协议逻辑

### `Content`

职责：
- 静态内容定义
- 背景定义加载
- Mod 物品加载
- 物品目录

主要提供给：
- `Gameplay`
- `GameBridge`
- `Agent`
- `CLI/TUI`

说明：
- 这是从 `AppShell/Core` 中抽出的内容子域

### `Commanding`

职责：
- 命令解析模型
- 命令结果模型
- 命令门控上下文
- 命令门控接口

主要提供给：
- `Gameplay`
- `Agent`
- `PluginRuntime`

说明：
- 这是命令协议层，不负责具体执行

### `Gameplay`

职责：
- 游戏主循环
- 命令门控
- 商店/礼物
- 宿主业务编排
- 副作用落地与状态应用

主要依赖：
- `Commanding`
- `Agent`
- `WorldState`
- `Content`
- `Executor`
- `Assets`

说明：
- 这是宿主业务编排层
- 不应长期保留与 `Executor` 并行的 LLM 推理链

### `Agent`

职责：
- 角色 Agent 运行时
- mailbox / dispatch
- 多角色协作编排
- 角色上下文与 runtime 生命周期

主要依赖：
- `Executor`
- `AgentCapabilities`
- `InterAgent`
- `Commanding`
- `Memory`
- `Content`

说明：
- `Agent` 应收敛为身份层和运行层
- 不应继续膨胀成新的底层推理能力层

### `AgentCapabilities`

职责：
- 统一 Agent 可见能力注册
- 合并插件工具、核心多角色能力与内建 agent 能力
- 提供能力执行抽象

主要提供给：
- `Agent`

说明：
- 这是 Agent 的能力视图层，负责把“宿主里有哪些能力”整理成 Agent / Executor 可消费的单一注册面

### `InterAgent`

职责：
- 角色间消息协议
- 角色对角色询问
- 角色对角色委托
- 角色间通信执行抽象

主要提供给：
- `AgentCapabilities`
- 后续的 `Dialogue`

说明：
- 这是多角色真实通信的协议层，不负责角色人格与通用推理

### `Executor`

职责：
- 小型结构化任务执行器抽象
- LLM executor 基础设施
- 结构化 JSON 输出解析

主要依赖：
- `Llm`
- `Memory`
- `Embedding`
- `WorldState`

主要提供给：
- `Agent`
- `Gameplay`
- 后续统一 turn pipeline

说明：
- 这是统一推理任务层
- 不拥有 Agent 身份、mailbox 与宿主副作用
- 后续应成为角色回合处理的唯一底层推理执行面

### `PluginRuntime`

职责：
- 插件进程宿主
- JSON-RPC 通信
- 工具/命令注册
- 插件生命周期

主要依赖：
- `Commanding`

说明：
- 应保持“运行时”属性，不承载具体游戏业务

### `GameBridge`

职责：
- `game/*` 反向 API 桥接
- 插件请求到业务服务的映射
- player / shop / gift / state / timeline / asset 等 handler 聚合

主要依赖：
- `Gameplay`
- `Agent`
- `WorldState`
- `Assets`
- `PluginRuntime`

说明：
- 这是业务桥接层，不应成为第二套 AI 主链入口
- 旧 `narrative / ai` handler 应逐步收口或转为薄代理

### `WorldState`

职责：
- 状态系统
- 时间系统
- 时间线系统

主要提供给：
- `Gameplay`
- `GameBridge`

说明：
- 这是全局世界推进与状态存取子域

### `Assets`

职责：
- 资产定义
- 资产记录
- blob/meta/link/unlock

主要提供给：
- `GameBridge`

说明：
- 这是独立资产子域

### `Llm`

职责：
- LLM 调用
- Provider 适配

主要提供给：
- `Memory`
- `Agent`
- `Executor`

说明：
- 这是基础能力模块，不应拥有玩法逻辑

### `Embedding`

职责：
- 文本向量化
- embedding provider 适配

主要提供给：
- `Memory`
- `Executor`

说明：
- 已从 `Llm` 中拆出
- 不承载聊天模型逻辑

### `Memory`

职责：
- 结构化记忆写入
- 语义记忆召回
- 向量检索与关键词回退

主要依赖：
- `Embedding`

主要提供给：
- `Agent`
- `Executor`

说明：
- 当前优先为 `Executor` 服务
- 其他旧业务调用点已被显式切断或标记为 TODO

## 当前治理优先级

1. 让 `Gameplay` 退出“自带 prompt + 自己直调 llm”的模式，改为编排 `Executor`。
2. 让 `Agent` 继续下接 `Executor`，自身收敛为 runtime / orchestration。
3. 继续收窄 `GameBridge`，避免重新长出旧式 AI 旁路。
4. 逐步清理模块对根级 `Core` / `Infrastructure` 的依赖泄漏，让目录边界变成真实架构边界。
3. `GameBridge -> Gameplay / WorldState / Assets / Content / Llm / Memory`
4. `Agent -> Executor / AgentCapabilities / Commanding / Memory / Content`
5. `AgentCapabilities -> InterAgent / Commanding`
6. `InterAgent -> Agent`
7. `PluginRuntime -> Commanding`
8. `Memory -> Embedding`

避免的方向：

1. `Agent Core -> PluginRuntime 模型`
2. `Content -> AppShell`
3. `WorldState -> Gameplay 编排接口`
4. `Llm -> Gameplay`
5. `Assets -> GameBridge`

## 当前判断

当前拆分还不算过细，原因：

1. 每个模块都对应真实子域
2. 模块职责大多能用一两句话清楚描述
3. 依赖图虽然仍有汇聚点，但没有出现明显的碎片化“空模块”

但已经接近一个临界点：

1. 再继续拆更小模块前，应优先清理少数剩余硬耦合
2. 优先做边界收口，而不是继续增加模块数量

当前更合理的方向是：

1. 维持这一级模块粒度
2. 继续减少公共契约层的跨模块类型泄漏
3. 让 `Core` 只保留真正的宿主内核契约
