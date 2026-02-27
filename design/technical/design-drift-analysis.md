# 设计文档漂移分析（2026-06，已更新）

本文档分析 `design/` 目录中所有设计文档与当前代码基线的匹配度，标记漂移程度。2026-06 更新：反映 `[GameApi]` 属性驱动路由重构和 `SessionScopedGameApi` 落地。

---

## 1. 漂移总览

| 漂移等级 | 文档数 | 说明 |
|----------|--------|------|
| ✅ 准确 | ~16 | 与代码一致或描述了正确的目标 |
| ⚠️ 部分过时 | ~16 | 主体正确但细节/路径/名称已变 |
| ❌ 严重过时 | ~9 | 核心概念/架构已发生根本变化 |

---

## 2. 逐文档分析

### 2.1 design/technical/ (技术设计 — 26 个文件)

#### architecture.dot — ✅ 已更新（2026-06）

**状态**：已更新为当前架构。新增 `IGameSession`、`ISessionGameApi`、`SessionScopedGameApi` 节点，反映双路径访问模型（`SubmitAsync` + `.Api.*`）。

#### architecture-session-current.dot — ✅ 已更新（2026-06）

**状态**：已更新为当前架构。新增 `session_api`（`ISessionGameApi` / `SessionScopedGameApi`）节点，反映 CLI/TUI/QQBot 通过 `.Api` 属性进行数据查询的路径。

#### architecture-plugin.dot — ✅ 已更新（2026-06）

**状态**：已更新为当前架构。新增 `GameApiDispatcher` 节点，新增 frontend cluster（`ISessionGameApi` → `SessionScopedGameApi`），反映属性驱动 dispatch 和前端双层访问模型。

#### architecture-agent.dot — ❌ 严重过时

**问题**：
- `CompanionOrchestrator` 不存在（职责由 `PlayerChatDispatcher` + `MultiCharacterOrchestrator` 分担）
- `CharacterTurnCoordinator` + `Character*Stage` 不存在（已被 flow-based 架构取代）
- `UnifiedAgentCapabilityRegistry` / `UnifiedAgentCapabilityExecutor` 名称不对（实际是 `IAgentCapabilityRegistry` / `IAgentCapabilityExecutor`）
- 缺少 `IAgentFlowRunner` 和 flow node executors
- 缺少 chat hook stage 链

#### architecture-memory.dot — ❌ 严重过时

**问题**：
- `RagMemoryService` 不存在（实际是 `MemoryRecallCoordinator` + `MemoryCommitCoordinator`）
- `NarrativeApiHandler` 不存在（已删除）
- 缺少 `IMemoryWriteProjector`、`AgentMemoryStoreAdapter`、`CompositeMemoryRetriever`
- `RerankClient` 不存在（无独立 rerank 客户端）
- 缺少 keyword retriever 回退路径

#### target-session-runtime-architecture.dot — ✅ 大体准确但需更新标记

**状态**：这张图是**目标**设计，不是当前事实。关键变化是：目标中的大部分已成为现实。
- SessionRuntime 模块已存在
- IGameSessionRuntime 已实现
- SessionInput / GameEvent 已落地
- CLI/TUI 已通过适配器模式接入
- GUI / HTTP / WebSocket adapter 尚未实现（仍然是目标）
- `Interaction` 模块仍然不存在（图上把它列为支持服务，实际功能在 Executor + Agent 中）

#### architecture-analysis.md — ❌ 严重过时（已替换为 architecture-analysis-current.md）

**问题**：
- 引用旧路径（`CLI/Program.cs`、`Modules/Interaction`）
- 列出不存在的测试目录
- 描述旧 CompanionOrchestrator 链路

#### architecture.md — ✅ 已更新（2026-06）

**状态**：已更新。路径引用修正为 `openLuo-cli`，移除 `Interaction` 模块引用，描述 `IGameSession` / `ISessionGameApi` 为已实现，新增 `GameApiDispatcher` + `[GameApi]` dispatch 说明，`IGameSessionRuntime` 描述从"目标"改为"已实现"。

#### architecture-guide.md — ✅ 已更新（2026-06）

**状态**：已更新。读代码入口路径已修正，新增 SessionRuntime 作为读代码入口，"加新 game/* API" 章节改为 `[GameApi]` 属性 + `GameApiDispatcher` 模式。

#### architecture-scorecard.md — ✅ 已更新（2026-06）

**状态**：已更新。评分反映 `[GameApi]` 重构收益（可维护性 7→8、协议一致性 7→8），`game/*` API 收口从"下一阶段"移至"已完成"。

#### character-agent-input-spec.md — ⚠️ 需要验证

设计文档描述的结构化 prompt context 输入与当前 `CharacterPromptContextBuilder` 实现可能一致，但需逐字段核对。

#### character-turn-pipeline.md — ❌ 严重过时

如果仍描述旧的 `CharacterTurnCoordinator → Character*Stage` 流水线，则已完全不适用。当前是 flow-based 架构。

#### content-registry-bootstrap.md — ✅ 大体准确

ContentRegistry 和 SessionBootstrapper 的模式与设计一致。

#### content-schema.md — ✅ 大体准确

Schema 定义与实际 `Content/Core/Definitions/` 一致。

#### database-schema.md — ⚠️ 需要验证

SQLite schema 文档需要与实际 `DatabaseInitializer` 中的建表语句对比。

#### executor-contract-guidelines.md — ✅ 大体准确

Executor 的 IExecutor<TInput,TOutput> 合约模式与设计一致。

#### gamebridge-api-surface.md — ✅ 已更新（2026-06）

**状态**：已更新。新增 Section 6（~280 行），完整记录 `[GameApi]` 属性驱动架构、`GameApiDispatcher` 路由扫描与运行时绑定、`ISessionGameApi` / `SessionScopedGameApi` 前端访问层、双层访问控制矩阵。

#### implementation-status.md — ✅ 已更新（2026-06）

**状态**：已更新。新增 `[GameApi]` 属性驱动路由、`ISessionGameApi` / `SessionScopedGameApi`、`IGameSession` / `IGameSessionCatalog` 为已完成项。

#### input-flow-runtime.md — ✅ 大体准确

数据流描述与当前代码一致，Mermaid 图准确。

#### io-architecture.md — ✅ 已更新（2026-06）

**状态**：已更新。新增 Section 2（前端数据查询，`session.Api.*`），Section 1 改为 `IGameSession.SubmitAsync`，Section 4 插件 IO 补充 `GameApiDispatcher` dispatch 说明。

#### memory-recall-executor.md — ⚠️ 需要验证

Memory recall executor 设计需要与当前 `MemoryRecallExecutor` 实现对比。

#### multimedia-output-architecture.md — ✅ 大体准确

多媒体输出架构与当前 `MessageOutputEvent` / `AssetMessageOutputPart` 一致。

#### planned-execution-schema-convergence.md — ✅ 大体准确

PlannedExecution 设计落地为 `PlannedExecutionPlanExecutor`。

#### plugin-hook-api-refactor-draft.md — ✅ 准确（草案）

这是最近的草案，描述的重构方向正在落地。新增的 `OnChatAfterInput/Output`、`OnToolExecutedInput/Output`、`HookTimeSnapshot`、`IResourceAwareHookContext` 已存在于代码中。

#### rag-memory.md — ⚠️ 部分过时

如果仍描述 `RagMemoryService`，则需要更新为当前的 `MemoryRecallCoordinator` + `MemoryCommitCoordinator` 架构。

#### resource-system.md — ✅ 大体准确

资源系统的三层组织与当前代码（`ResourceDefinition`、插件提供的资源、`IResourceCatalogService` 等）一致。

### 2.2 design/gameplay/ (玩法设计 — 6 个文件)

这些文件描述的是**玩法概念**而非具体代码架构，漂移程度较低：

- `character-system.md` — ✅ 大体准确
- `commands-reference.md` — ⚠️ 命令列表需要更新以反映实际插件命令
- `core-mechanics.md` — ✅ 概念层面准确
- `economy.md` — ✅ 概念层面准确
- `narrative-beats.md` — ⚠️ 叙事节拍体系已从旧 narrative 引擎迁移到 flow-based 架构
- `time-system.md` — ✅ 与 WorldState 时间系统一致

### 2.3 design/plugin/ (插件设计 — 3 个文件)

- `plugin-spec.md` — ⚠️ 可能需要更新以反映 typed hook 和新的 hook 类型
- `plugin-dev-guide.md` — ⚠️ 开发者指南需要更新 hook 编写方式
- `mcp-protocol.md` — ⚠️ 协议描述基本准确但需补充新 hook 类型

### 2.4 design/implementation/ (实施文档 — 3 个文件)

- `implementation-status.md` — ❌ 需要全面更新
- `roadmap.md` — ⚠️ 路线图需要更新已完成项
- `testing-strategy.md` — ⚠️ 测试策略可能需要更新

### 2.5 design/todo/ (待办 — 2 个文件)

- `unified-session-runtime-architecture.md` — ✅ 文档自身已在开头标注状态更新（2026-05），准确反映了哪些已完成、哪些尚未完成
- `unified-session-runtime-roadmap.md` — ⚠️ 路线图需要标记已完成项

### 2.6 其余目录

- `design/background/` — ⚠️ background 概念已演化为 character archetype + plugin config
- `design/mod/` — ⚠️ mod 概念已演化为 item-packs + plugin overrides
- `design/story/` — ✅ 角色设定和世界观与代码无关，准确

---

## 3. 关键漂移模式

### 3.1 模块重命名/重组

| 旧设计文档中的名称 | 实际代码中的名称/状态 |
|-------------------|----------------------|
| `Agent Kernel` | 不存在独立模块 — 功能在 Agent + Executor 中 |
| `Interaction` | 不存在独立模块 — 功能在 Executor + Agent 中 |
| `Mods` | `item-packs` + plugin config overrides |
| `Backgrounds` | `CharacterArchetypeDefinition` |
| `CompanionOrchestrator` | `PlayerChatDispatcher` + `MultiCharacterOrchestrator` |
| `CharacterTurnCoordinator` + `Character*Stage` | Flow-based: `IAgentFlowRunner` + FlowNodeExecutors |
| `RagMemoryService` | `MemoryRecallCoordinator` + `MemoryCommitCoordinator` |
| `NarrativeApiHandler` | 已删除 |
| `AiApiHandler` | 已删除 |
| `UnifiedAgentCapabilityRegistry` | `IAgentCapabilityRegistry` |

### 3.2 目标已实现

以下"目标架构"中的设计已经落地为实际代码：
- `Modules/SessionRuntime` 模块
- `IGameSessionRuntime` 统一门面
- `IGameSession` / `IGameSessionCatalog` per-client session 管理
- `ISessionGameApi` / `SessionScopedGameApi`（21 方法，自动注入 gameId）
- `SessionInput` / `GameEvent` 结构化协议
- Session / Channel / Visibility 模型
- CLI/TUI/QQBot 不再直接依赖 `IGameEngine` / `IAgentRuntimeHub`
- Flow-based Agent 架构
- Typed hook 系统（`hooks/call` 路径）
- Resource 系统的 `game/resource/*` API
- `[GameApi]` 属性驱动路由 + `GameApiDispatcher`（44 routes, 10 handlers, 启动时扫描）
- `GameApiHandler` 退化为 ~70 行薄层代理

### 3.3 文件路径变更

| 旧路径（设计文档） | 新路径（实际） |
|--------------------|---------------|
| `openLuo/CLI/Program.cs` | `openLuo/Program.cs` |
| `openLuo/TUI/TuiApplication.cs` | `openLuo/Interfaces/TUI/TuiApplication.cs` |
| `openLuo/Modules/Agent/.../CompanionOrchestrator.cs` | 不存在 |
| `openLuo/Modules/Agent/.../CharacterTurnCoordinator.cs` | 不存在 |
| `openLuo/Modules/Interaction/` | 不存在 |

---

## 4. 需要优先更新的文档（按优先级，2026-06 更新）

### P0 — 已修复（2026-06 [GameApi] 重构）
1. `architecture.dot` — ✅ 已更新
2. `architecture-plugin.dot` — ✅ 已更新
3. `architecture-session-current.dot` — ✅ 已更新
4. `gamebridge-api-surface.md` — ✅ 已更新（新增 Section 6）

### P0 — 仍需修复
5. `architecture-agent.dot` — ❌ 需要重绘（flow-based 架构）
6. `architecture-memory.dot` — ❌ 需要重绘（MemoryRecallCoordinator 架构）

### P1 — 已修复（2026-06 全面更新）
7. `architecture-analysis.md` — 已替换为 `architecture-analysis-current.md`
8. `architecture.md` — ✅ 已更新
9. `architecture-guide.md` — ✅ 已更新
10. `architecture-scorecard.md` — ✅ 已更新
11. `io-architecture.md` — ✅ 已更新
12. `implementation-status.md` — ✅ 已更新
13. `plugin-hook-api-refactor-draft.md` — ✅ 已更新（补充 GameApiDispatcher）
14. `design-drift-analysis.md` — ✅ 本文档已更新

### P1 — 仍需修复
15. `character-turn-pipeline.md` — 需要重写为 flow-based 描述
16. `rag-memory.md` — 更新为当前 Memory 架构

### P2 — 细节更新
17. `plugin-spec.md` / `plugin-dev-guide.md` / `mcp-protocol.md` — 已补充 [GameApi] dispatch 说明
18. `implementation/roadmap.md` — 标记已完成项
19. `todo/unified-session-runtime-roadmap.md` — 标记已完成项
