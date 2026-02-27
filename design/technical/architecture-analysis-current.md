# openLuo 当前架构分析（2026-05）

本文档基于对 `openLuo/` 代码基线（~226 个 .cs 文件，16 个模块）的完整阅读，
替代旧的 `architecture-analysis.md`，反映当前真实的架构状态。

---

## 1. 系统定位

openLuo 是一套**可扩展 AI 角色引擎**，以命令驱动为入口、以角色 Agent 为核心执行
层、以 pack/schema 组织内容扩展、以 Python 插件承载运行时扩展、以 SQLite 为持久化底座。

当前运行形态：

| 维度 | 技术选型 |
|------|----------|
| 宿主语言 | C# .NET 10 |
| 入口形态 | CLI / TUI / QQBot |
| 内容扩展 | canonical schema + registry/bootstrap（JSONC 定义） |
| 运行时扩展 | Python 插件，JSON-RPC over stdio（MCP 协议） |
| 持久化 | SQLite + sqlite-vec（向量检索） |
| 模型层 | LLM / Embedding 可替换（OpenAI / Ollama / Qwen / DeepSeek） |

---

## 2. 分层架构

当前架构采用 **模块化单体 + DDD 分层** 模式，每一层有清晰的职责边界：

```
┌─────────────────────────────────────────────────────┐
│                  Entry / Presentation                 │
│  Program.cs → Bootstrapper → CLI | TUI | QQbot       │
├─────────────────────────────────────────────────────┤
│               Application Runtime                     │
│  SessionRuntime (IGameSessionRuntime)                │
│  Session / Channel / InputRouter / OutputEventBus    │
├─────────────────────────────────────────────────────┤
│               Domain Orchestration                    │
│  Gameplay (GameEngine)  │  Agent Runtime             │
├─────────────────────────────────────────────────────┤
│               Support Services                        │
│  Executor │ Memory │ WorldState │ Assets │ Content   │
│  PluginRuntime │ GameBridge │ Llm │ Embedding        │
│  Commanding │ AgentCapabilities │ InterAgent         │
│  AppShell                                              │
├─────────────────────────────────────────────────────┤
│               Persistence                             │
│  SQLite + sqlite-vec                                  │
└─────────────────────────────────────────────────────┘
```

### 2.1 Entry / Presentation

**实际代码路径**：`Program.cs` → `OpenLuoBootstrapper.BootstrapAsync()` →
`OpenLuoRuntimeContext` → CLI/TUI/QQbot

关键事实：
- `Program.cs` 只负责解析 `LaunchOptions`、调用 `BootstrapAsync`、选择交互模式
- `OpenLuoBootstrapper` 负责配置读取、DI 装配、数据库初始化、插件加载、session 打开
- 返回的 `OpenLuoRuntimeContext` 包含 `IGameSessionCatalog`、`IGameSession`、`IGameStreams`、`GameState?`
- CLI/TUI/QQbot **不直接依赖** `IGameEngine` 或 `IAgentRuntimeHub`，而是通过 `IGameSession` 间接使用

### 2.2 Application Runtime — SessionRuntime 模块

**这是目标架构设计中最关键的成果——已经落地实现。**

`Modules/SessionRuntime/` 提供了统一的应用门面层：

| 组件 | 实际文件 |
|------|----------|
| 统一门面接口 | `Core/Interfaces/IGameSessionRuntime.cs` |
| 门面实现 | `Application/GameSessionRuntime.cs` |
| 会话目录 | `Application/GameSessionCatalog.cs` |
| 会话注册表 | `Application/InMemorySessionRegistry.cs` |
| 输入路由 | `Application/DefaultInputRouter.cs` |
| 输出事件总线 | `Application/InMemoryOutputEventBus.cs` |
| 会话引导 | `Application/ContentRegistrySessionBootstrapper.cs` |
| 上下文访问器 | `Application/AsyncLocalSessionExecutionContextAccessor.cs` |

数据流：
```
CLI/TUI/QQbot → IGameSession.SubmitAsync(GameSessionInput)
  → GameSessionRuntime.SubmitAsync(SessionInput)
    → IInputRouter.RouteAsync()
      → IGameEngine.ExecuteAsync()
        → IAgentInvocationRouter.ExecuteAsync()
    → IOutputEventBus 发布 GameEvent 序列
```

### 2.3 Domain Orchestration

#### Gameplay 模块

`Modules/Gameplay/` — 游戏引擎与玩法核心。

| 组件 | 文件 |
|------|------|
| 游戏引擎 | `Application/Services/GameEngine.cs` |
| 命令门控 | `Application/Services/CommandGate.cs` |
| 状态评估 | `Application/Services/StateEvaluationCoordinator.cs` |
| 状态聚合 | `Application/Services/StatusAggregator.cs` |
| 商店服务 | `Application/Services/ShopService.cs` |
| 礼物服务 | `Application/Services/GiftService.cs` |
| 命令确认 | `Application/Services/CommandConfirmationService.cs` |

`GameEngine` 的主要职责：
1. `ExecuteAsync(gameId, rawInput)` — 解析命令（`/` `$` `@` `&` 四种前缀），路由到 `AgentInvocationRouter`
2. `InitializeAsync(gameId, archetypeId, playerName)` — 初始化新游戏
3. `GetStateAsync(gameId)` — 获取游戏状态

关键变化：`GameEngine` 不再由表现层直接调用，而是由 `GameSessionRuntime` →
`IInputRouter` 间接调用。

#### Agent 模块（79 文件，最大模块）

`Modules/Agent/` — 角色 Agent 运行时与编排。

**调度链**（实际代码路径）：
```
GameEngine → AgentInvocationRouter
  ├── PlayerChatDispatcher     (处理 /chat 等聊天命令)
  ├── MultiCharacterOrchestrator (处理 /task, /switch 等多角色命令)
  └── IAgentCommandBridge      (处理插件注册的动态命令)
```

`AgentInvocationRouter` 是三路分发器：
1. **PlayerChat**：玩家聊天 → `PlayerChatDispatcher` → `AgentRuntimeHub` → `AgentDispatcher` → `DefaultAgentMessageHandler`
2. **MultiCharacter**：多角色任务 → `MultiCharacterOrchestrator` → `PartyTaskRepository`
3. **Plugin Commands**：插件命令 → `PluginAgentCommandBridge` → `McpPluginHost`

**角色回合执行链**（flow-based，不再使用旧的 Stage 模式）：
```
DefaultAgentMessageHandler → CharacterAgent.RunTurnAsync()
  → IAgentFlowRunner.RunAsync(flowId)
    → CharacterMemoryRecallFlowNodeExecutor  (记忆召回)
    → CharacterPlanFlowNodeExecutor          (规划)
    → CharacterPlannedExecutionFlowNodeExecutor (按计划执行)
    → CharacterToolUseFlowNodeExecutor       (工具调用)
    → CharacterResponseFlowNodeExecutor      (生成回复)
    → CharacterStateUpdateFlowNodeExecutor   (状态更新)
    → CharacterFinalizeReplyFlowNodeExecutor (完成)
```

**Chat Hook 链**（在 `PlayerChatDispatcher` 中）：
```
IAgentChatHookStage (有序链):
  1. QqBotReplyStyleChatHook     (QQbot 回复风格)
  2. PluginChatHookStage         (插件 onPromptContext)
  3. GiftIntentChatHook          (礼物意图检测)
  4. PostChatStateEvaluationHook (状态评估)
```

**关键差异 vs 旧设计文档**：
- 旧文档描述的 `CompanionOrchestrator` 不存在，职责由 `PlayerChatDispatcher` + `MultiCharacterOrchestrator` 分担
- 旧文档描述的 `CharacterTurnCoordinator` + `Character*Stage` 不存在，已被 flow-based 架构取代
- 旧文档描述的 `UnifiedAgentCapabilityRegistry` / `UnifiedAgentCapabilityExecutor` 实际名称为 `IAgentCapabilityRegistry` / `IAgentCapabilityExecutor`

### 2.4 Support Services

#### Executor 模块（38 文件）

`Modules/Executor/` — LLM 执行器层，负责结构化的 LLM 调用。

| 执行器 | 用途 |
|--------|------|
| `CharacterResponseExecutor` | 角色回复生成 |
| `MemoryRecallExecutor` | 记忆召回 |
| `PlanExecutor` | 计划生成 |
| `PlannedExecutionPlanExecutor` | 按计划执行 |
| `ToolUseExecutor` | 工具使用决策 |
| `StateUpdateExecutor` | 状态更新 |
| `FlowRoutingExecutor` | 流程路由 |
| `GiftIntentExecutor` | 礼物意图 |
| `RandomImageFetchExecutor` | 随机图片 |

每个执行器有独立的 Input/Output 类型和 PromptBuilder，统一实现 `IExecutor<TInput, TOutput>`。

#### Memory 模块（19 文件）

`Modules/Memory/` — 记忆系统。

实际架构（非旧文档的 `RagMemoryService`）：
```
Agent → IAgentMemoryStore (AgentMemoryStoreAdapter)
  → IMemoryWriteService (MemoryCommitCoordinator)
    → IMemoryWriteProjector (DefaultMemoryWriteProjector)
      → IMemoryRepository (SqliteMemoryRepository) → SQLite
  → IMemoryRecallService (MemoryRecallCoordinator)
    → IMemoryRetriever (CompositeMemoryRetriever)
      ├── VectorMemoryRetriever  → sqlite-vec
      └── KeywordMemoryRetriever → SQLite FTS
```

#### WorldState 模块（40 文件）

`Modules/WorldState/` — 世界状态、时间线和资源系统。

三个子系统：
1. **State**：`StateDef` → `StateDefStore` → `StateRegistry` → `StateStore` → `StateMutationService` / `StateQueryService` → `StateSnapshotBuilder`
2. **Time**：`ITimeProvider`（VirtualTime / RealtimeTime / DisabledTime）→ `ITimeService` → `TimeSnapshot`
3. **Resources**（新增）：`IResourceCatalogService` / `IResourceValueService` / `IResourceStatusProjectionService` / `IResourceEvaluationProjectionService` / `IResourceLifecycleService`

#### PluginRuntime 模块（17 文件）

`Modules/PluginRuntime/` — MCP 插件运行时。

实际架构：
```
McpPluginHost (IPluginHost)
  ├── LoadAllAsync() → 扫描 data/plugins/*/plugin.jsonc
  ├── tools/list → 收集所有插件的命令注册
  ├── tools/call → 转发到具体 PluginProcess
  ├── hooks/call → typed hook 调用
  └── 反向 game/* API 通过 IGameApiMediator
```

Hook 上下文类型（已落地的新 typed DTO）：
- `OnPromptContextInput` / `OnPromptContextOutput`
- `OnStatusQueryInput` / `OnStatusQueryOutput`
- `OnChatAfterInput` / `OnChatAfterOutput`（新增）
- `OnToolExecutedInput` / `OnToolExecutedOutput`（新增）
- `HookTimeSnapshot`（新增统一时间上下文）
- `IResourceAwareHookContext`（新增资源感知接口）

#### GameBridge 模块（12 文件）

`Modules/GameBridge/` — 插件反向调用宿主的 API 网关。

**Attribute-Driven Dispatch（2026-06 重构）**：

`GameApiDispatcher`（新增）替代了旧的手动 switch 路由表，采用 `[GameApi("route")]` 特性注解驱动分发：
- 44 个路由通过 `[GameApi("route")]` 特性直接注解在 handler 方法上
- `GameApiDispatcher` 启动时扫描全部 10 个 handler 类型，收集所有 `[GameApi]` 方法构建路由表
- 运行时 `DispatchAsync()` 匹配路由、自动注入 `gameId`（从 `bridgeContext.GameId`）、自动将 JSON 参数绑定到 C# 强类型参数
- 所有 44 个 handler 方法已从 `JsonNode? p` 重构为强类型参数签名

`GameApiHandler` 现在是约 70 行的薄代理层，仅委托到 `GameApiDispatcher.DispatchAsync()`（旧实现约 230 行的手动 switch 语句）。

当前路由覆盖的领域：
```
game/session/*      → GameStateApiHandler
game/character/*    → PlayerApiHandler
game/inventory/*    → PlayerApiHandler
game/items/*        → PlayerApiHandler
game/shop/*         → ShopApiHandler
game/gift/*         → GiftApiHandler
game/affection/*    → PlayerApiHandler
game/commands/*     → HostBridgeApiHandler
game/lifecycle/*    → LifecycleApiHandler
game/diary/*        → LifecycleApiHandler
game/ui/*           → HostBridgeApiHandler
game/log            → HostBridgeApiHandler
game/state/*        → StateApiHandler
game/resource/*     → ResourceApiHandler
game/asset/*        → AssetApiHandler
game/timeline/*     → TimelineApiHandler
```

**关键变化 vs 旧设计**：
- 旧 `game/narrative/*` 已删除（正确）
- 旧 `game/llm/*` 已删除（正确）
- 旧 `game/agent/plan` 已删除（正确）
- 新增 `game/resource/*`（ResourceApiHandler）
- 新增 `game/asset/*`（AssetApiHandler，已扩展）
- 路由注册从手动 switch 迁移到 `[GameApi]` 特性注解，消除手动路由维护

#### SessionScopedGameApi

`Modules/GameBridge/Core/` — 面向 frontend 调用方的会话作用域 API。

- `ISessionGameApi` 接口暴露 21 个方法，无需 `gameId` 参数
- `SessionScopedGameApi` 实现从 `SessionHandle` 自动解析 `gameId`，内部委托到 `GameApiDispatcher`
- `IGameSession.Api` 属性暴露 `ISessionGameApi` 实例，供 CLI/TUI/QQbot 等 frontend 直接调用
- `ISessionGameApiFactory` 注册为 singleton，按需创建 per-session 实例

#### 其余模块

| 模块 | 文件数 | 用途 |
|------|--------|------|
| Content | 21 | 内容定义加载（角色原型、物品、插件配置、技能/工具文档）|
| Assets | 21 | 资产系统（blob 存储、元数据、链接、解锁）|
| Llm | 14 | LLM 客户端工厂 + 多 Provider（OpenAI/Ollama/Qwen/DeepSeek）|
| Embedding | 5 | Embedding 客户端 + Provider 路由 |
| Commanding | 4 | 命令语义定义（`/` `$` `@` `&` 前缀 + CommandDescriptor）|
| AgentCapabilities | 3 | Agent 能力注册表 |
| AppShell | 3 | DI 装配（`ServiceCollectionExtensions.AddOpenLuo()`）|
| InterAgent | 3 | 角色间消息传递 |

---

## 3. 数据持久化

### 数据库表（通过代码推断）

- `game_state` — 游戏存档（player_name, archetype_id, current_day, current_minute, active_character_id 等）
- `characters` — 角色数据（name, archetype_id, is_enabled, display_priority 等）
- `state_defs` — 状态定义（注册表）
- `state_store` — 状态值存储（快照值）
- `asset_defs` — 资产定义（注册表）
- `asset_store` — 资产记录
- `asset_blobs` — 资产二进制数据
- `asset_meta` — 资产元数据
- `asset_links` — 资产链接（entity links）
- `asset_unlocks` — 资产解锁记录
- `memories` — 记忆行
- `memory_vectors` — 向量索引（sqlite-vec）
- `inventory` — 库存
- `shop_offers` — 商店商品
- `party_tasks` / `party_task_steps` — 多角色任务
- `agent_context` — Agent 上下文（对话历史）
- `timeline_events` — 时间线事件

---

## 4. 当前稳定区

- CLI/TUI/QQbot 三入口
- SessionRuntime 统一门面层
- 数据库初始化与迁移
- 插件发现与动态命令注册
- State / Timeline / Asset / Resource 基线
- Character Agent flow-based 回合执行
- `/chat`、`/task`、`/switch` 命令
- Inter-agent `ask_character` 与 session chat
- 结构化 GameEvent 输出模型（MessageOutputEvent、StatusSnapshotEvent 等）

## 5. 当前复杂区

### 5.1 Agent 调度链仍然较长

`GameEngine → AgentInvocationRouter → PlayerChatDispatcher → AgentRuntimeHub → AgentDispatcher → DefaultAgentMessageHandler → CharacterAgent → IAgentFlowRunner → 7个 FlowNodeExecutor`

这条链是系统能力最强的部分，也是维护成本最高的部分。但 flow-based 架构比旧的 Stage 模式更灵活。

### 5.2 GameBridge 曾是宽接口，已通过特性注解改善

`GameApiHandler` 当前覆盖 session/state/resource/asset/timeline/character/inventory/shop/gift/commands/lifecycle/diary 等 12 个领域（44 个路由），是系统中最宽的接口。2026-06 重构将手动 switch 路由表迁移到 `[GameApi]` 特性注解 + `GameApiDispatcher` 自动扫描，消除了手动路由维护工作。

### 5.3 DI 装配中心仍然偏重

`ServiceCollectionExtensions.AddOpenLuo()` 方法约 340 行，注册了 100+ 个服务。这是单点装配中心的自然结果，但随着模块增长，可能需要拆分为模块级注册扩展方法。

---

## 6. 模块依赖关系（当前实际）

```
Program.cs
  └── OpenLuoBootstrapper
        └── ServiceCollectionExtensions.AddOpenLuo()
              ├── AppShell (配置、DI)
              ├── SessionRuntime (会话门面)
              │     ├── IGameEngine (Gameplay)
              │     │     └── IAgentInvocationRouter (Agent)
              │     │           ├── PlayerChatDispatcher → AgentRuntimeHub → ...
              │     │           ├── MultiCharacterOrchestrator
              │     │           └── IAgentCommandBridge → McpPluginHost
              │     └── IOutputEventBus → GameEvent 序列
              ├── WorldState (State / Time / Resources)
              ├── Assets (Asset Store / Blob / Meta / Link)
              ├── Memory (Recall / Write / Vector)
              ├── Executor (LLM 执行器)
              ├── Content (定义加载 / Registry)
              ├── Llm (LLM 客户端)
              ├── Embedding (Embedding 客户端)
              ├── PluginRuntime (MCP 插件主机)
              │     └── GameBridge (game/* API 网关)
              ├── Commanding (命令语义)
              ├── AgentCapabilities (能力注册)
              └── InterAgent (角色间消息)
```

---

## 7. 与目标架构的差距

目标架构（`unified-session-runtime-architecture.md`）的核心诉求已经**大部分实现**：

| 目标 | 状态 |
|------|------|
| SessionRuntime 模块存在 | ✅ 已实现 |
| IGameSessionRuntime 统一门面 | ✅ 已实现 |
| SessionInput 统一输入 | ✅ 已实现 |
| GameEvent 结构化输出 | ✅ 已实现 |
| Session / Channel / Visibility | ✅ 已实现 |
| CLI/TUI 不直接依赖 IGameEngine | ✅ 已实现 |
| CLI/TUI 不直接依赖 IAgentRuntimeHub | ✅ 已实现 |
| Program.cs 收口 | ✅ 已大幅改善（~40行） |
| PluginRuntime / GameBridge 保留现有架构 | ✅ 保持 |
| 富媒体输入进入 Agent 主链 | ⚠️ 附件接入已支持，但多媒体理解仍以文本+附件为主 |
| TextOutputEvent 兼容层 | ⚠️ 仍保留，但 MessageOutputEvent 已支持结构化输出 |
| 多会话并发连接 | ⚠️ SessionRegistry 已支持多 session，但真正多客户端并发未充分检验 |

**未完成的关键缺口**：
1. `IGameStreams` 尚未完全被 `IOutputEventBus` 取代
2. 旧 `TextOutputEvent` 兼容层仍然保留
3. GUI / HTTP / WebSocket adapter 尚未实现（但框架已支持）
4. 真正多客户端并发连接同一游戏会话尚未充分验证
