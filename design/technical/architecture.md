# 当前架构总览

本文件与这些 `Graphviz DOT` 结构图对应：

- [architecture.dot](/home/yoitsuholo/Code/openLuo-cli/design/technical/architecture.dot)
- [architecture-session-current.dot](/home/yoitsuholo/Code/openLuo-cli/design/technical/architecture-session-current.dot)
- [architecture-agent.dot](/home/yoitsuholo/Code/openLuo-cli/design/technical/architecture-agent.dot)
- [architecture-plugin.dot](/home/yoitsuholo/Code/openLuo-cli/design/technical/architecture-plugin.dot)
- [architecture-memory.dot](/home/yoitsuholo/Code/openLuo-cli/design/technical/architecture-memory.dot)
- [target-session-runtime-architecture.dot](/home/yoitsuholo/Code/openLuo-cli/design/technical/target-session-runtime-architecture.dot)

## 1. 系统定位

`openLuo` 当前是一套以命令驱动为入口、以角色 Agent 为核心执行层、以 pack/schema 组织内容扩展、以 Python 插件承载主要运行时扩展、以 SQLite 为持久化底座的可扩展 AI 角色引擎。

当前运行形态：

- 宿主：C# `.NET 10`
- 入口：CLI / TUI / QQBot
- 内容扩展：canonical schema + registry/bootstrap
- 运行时扩展：Python 插件，JSON-RPC over stdio
- 持久化：SQLite + `sqlite-vec`
- 模型层：LLM / Embedding / Rerank 可替换

这份文档同时描述两件事：

- 当前代码基线的真实架构（2026-06）
- 推荐演进到的目标会话运行时架构

## 2. 当前态 System Context

当前主链：

1. 玩家进入 `Program.cs`
2. `Program` 负责配置读取、DI 装配、数据库初始化和插件加载
3. `Program` 再选择 CLI 模式、TUI 模式或 QQBot 模式
4. CLI/TUI/QQBot 通过 `IGameSession` 门面与 session 层交互
   - 命令/对话：`session.SubmitAsync(SessionInput)`
   - 数据查询：`session.Api.GetStateAsync()` 等（自动注入 gameId）
5. 宿主进程内部再驱动 Gameplay、Agent、Plugin Runtime、WorldState、Assets、Memory 和 LLM Integration
6. 数据持续写入 SQLite 与 `sqlite-vec`
7. Python 插件作为外部进程与宿主通过 stdio JSON-RPC 通信

## 3. 目标态总览

目标态见：

- [unified-session-runtime-architecture.md](/home/yoitsuholo/Code/openLuo-cli/design/todo/unified-session-runtime-architecture.md)
- [unified-session-runtime-roadmap.md](/home/yoitsuholo/Code/openLuo-cli/design/todo/unified-session-runtime-roadmap.md)
- [target-session-runtime-architecture.dot](/home/yoitsuholo/Code/openLuo-cli/design/technical/target-session-runtime-architecture.dot)

目标态的核心变化不是替换业务内核，而是在表现层与现有内核之间新增一个统一的 `Session Runtime` 层。截至 2026-06，SessionRuntime 模块已经落地为实际代码，`IGameSession` / `IGameSessionRuntime` / `IGameSessionCatalog` 已实现。

目标态关键点（大部分已实现）：

- CLI/TUI/QQBot/HTTP/WS 都只是 adapter ✅（CLI/TUI/QQBot 已适配）
- 所有 adapter 只依赖 `IGameSession`（命令/对话）和 `IGameSessionCatalog`（游戏管理）
- 输入统一为 `SessionInput` ✅
- 输出统一为 `GameEvent` ✅
- 引入 `Session / Channel / Visibility` ✅
- 启动、恢复、新建游戏、runtime 预热由应用层统一处理 ✅
- 前端数据查询通过 `session.Api.*`（`ISessionGameApi`），自动从 session handle 注入 gameId ✅
- GUI / HTTP / WebSocket adapter 尚未实现（仍然是目标）

## 4. 关于 DOT 分层

是的，`rank` 可以作为分层使用。

在 Graphviz DOT 里，这套文档采用两种方式共同表达层次：

- `rank=same`
  把同一层的节点放在同一条层线上
- `cluster_*`
  把同一职责块包成一个视觉分组

推荐的层次顺序是：

- Entry / Presentation
- Application Wiring 或 Session Runtime
- Domain / Orchestration
- Support Services
- Resources / External
- Persistence

也就是说，DOT 图中的“分层”不是装饰，而是架构语义本身。

## 5. 当前态图的分工

当前不再试图用一张图覆盖全部容器、组件和所有关系。

分工如下：

- `architecture.dot`：整体总览，只保留稳定容器和主依赖方向
- `architecture-session-current.dot`：专门描述当前 CLI/TUI 和高层接口的真实耦合点
- `architecture-agent.dot`：玩家命令进入 Agent 运行时后的关键执行链
- `architecture-plugin.dot`：插件进程、`game/*` API（通过 `[GameApi]` 属性 + `GameApiDispatcher` 驱动）和宿主桥接链
- `architecture-memory.dot`：记忆写入、召回、压缩和向量索引链

这样做的目的不是减少信息，而是把信息按阅读任务拆开，避免在一张图里产生过多回绕边。

## 6. 当前态 Container 层

container 视图对应当前稳定子域。

在入口层需要特别注意一件事：

- `Program.cs` 才是实际宿主入口
- `TuiApplication` 只是 `Program` 在 `--tui` 模式下创建的 UI 对象
- 因此 CLI/TUI 不应在图里被画成“直接依赖全部内核模块的两个程序”
- 更准确的画法是：`Program` 直接依赖装配和少量高层服务，CLI/TUI 只是它选择出的交互表面

### Program / UI 层

- `Program` 直接调用 `AddOpenLuo` 完成装配
- `Program` 直接解析并获取 `IGameSessionCatalog`，通过 `OpenSessionAsync` 创建 session
- CLI/TUI/QQBot 通过 `IGameSession.SubmitAsync` 提交输入，通过 `session.Api.*` 查询数据
- `IGameSession` 是表现层的统一门面

### Session Runtime（已实现）

- `IGameSessionCatalog`：管理游戏会话生命周期
- `IGameSession`：per-client session，暴露 `SubmitAsync` + `.Api`（`ISessionGameApi`）
- `ISessionGameApi`：21 个方法，自动从 SessionHandle 注入 gameId，前端无需传 gameId
- `IGameSessionRuntime`：内部 runtime 门面，管理输入路由和输出事件总线

### AppShell

- 管配置
- 管 DI
- 管数据库初始化
- 管模块装配

### Gameplay

- 主入口是 `GameEngine`
- 管命令主链、command gate、商店、礼物、状态聚合

### Commanding

- 提供统一命令语义
- 定义 `/`、`$`、`@`、`&` 四种前缀

### Content

- 加载 archetype / item / resource / tool / skill 定义
- 构建 `ContentRegistry`

### Agent Runtime

- 管角色 runtime、消息分发、上下文、任务状态、companion 命令

### Agent Kernel

- 管 tool-use loop
- 管 skill/tool/subagent 文档动态加载

### Agent Capabilities

- 管 Agent 的统一能力面

### InterAgent

- 管角色对角色提问和内部会话

### Plugin Runtime

- 管 Python 插件进程和 JSON-RPC 协议桥

### GameBridge

- 管插件反向调用 `game/*` 的宿主映射
- 通过 `[GameApi]` 属性标记 handler 方法，`GameApiDispatcher` 启动时扫描注册、运行时自动绑定参数
- `GameApiHandler` 已退化为 ~70 行的薄层代理（dispatch -> result -> JsonNode 转换）

### WorldState

- 管 state / time / timeline

### Assets

- 管资产定义、记录、blob、meta、link、unlock

### Memory

- 管记忆写入、检索、rerank、压缩

### LLM Integration

- 管 LLM、Embedding、Rerank 客户端

## 7. 当前态表现层耦合图

[architecture-session-current.dot](/home/yoitsuholo/Code/GimaiSeigatsu-cli/design/technical/architecture-session-current.dot) 专门回答一个问题：

- 当前 CLI/TUI 到底直接依赖了哪些高层接口？

这张图只保留：

- `Program`
- `CLI Mode`
- `TUI Application`
- `IGameEngine`
- `IAgentRuntimeHub`
- `IGameStreams`
- `IGameSessionRuntime`
- `IPluginHost`
- `DatabaseInitializer`

它的价值在于避免把“间接运行时依赖”误画成“入口层直接依赖”。

## 8. 当前态 Gameplay 组件流

关键数据流：

`Player -> CLI Mode / TUI Application -> IGameEngine -> CommandGate -> AgentInvocationRouter -> CompanionOrchestrator / PluginRuntime`

要点：

- CLI/TUI 在这一层并不直接操作 WorldState、Assets、Memory、LLM 等低层模块
- `GameEngine` 负责解析命令和统一执行主链
- `/chat`、`/task` 等走 Agent 侧
- 插件命令、tool、skill、subagent 通过 Plugin Runtime 侧执行

## 9. 当前态 Agent 组件流

关键数据流：

`IGameEngine -> AgentInvocationRouter -> CompanionOrchestrator -> AgentRuntimeHub -> AgentDispatcher -> CharacterAgentRuntime -> DefaultAgentMessageHandler -> CharacterAgent -> CharacterTurnCoordinator -> Character*Stage`

要点：

- `DefaultAgentMessageHandler` 只负责把 runtime message 转成角色回合请求，并应用回合结果
- `CharacterAgent` 是角色主体入口，内部通过 `CharacterTurnCoordinator` 编排单轮回合
- `Character*Stage` 分别承接 memory recall、plan、tool-use、response、state-update
- 旧 `AgentReasoner` 链路已删除；稳定推理能力应继续下沉到 `Executor`、`Memory`、`LLM`

## 10. 当前态 Plugin Runtime / GameBridge 组件流

关键数据流：

`PluginProcess -> GameApiHandler (thin proxy) -> GameApiDispatcher -> [GameApi]-annotated handlers`

要点：

- 宿主先通过 `tools/list` 建立插件命令面
- 插件执行时通过 `tools/call` 回到宿主
- 插件需要宿主数据时，再通过 `game/*` 方法反向调用
- `GameApiHandler` 是薄层 JSON-RPC 代理，实际路由由 `GameApiDispatcher` 通过反射扫描 `[GameApi]` 属性完成
- 10 个 handler 类上的 44 条 `[GameApi]` 路由在启动时全量扫描，运行时按路由查表 + 自动参数绑定
- 前端 C# 代码通过 `session.Api.*`（`ISessionGameApi` / `SessionScopedGameApi`）调用同一批 handler 方法，gameId 自动从 session handle 注入

## 11. 当前态 Memory 组件流

关键数据流：

`Agent / Narrative -> RagMemoryService -> SQLite / sqlite-vec / Embedding / Rerank / LLM`

要点：

- 记忆写入时先落普通表，再尝试落向量表
- 召回时优先向量检索，再经过 rerank；失败时回退到词法检索
- 老记忆压缩通过 LLM 生成摘要后再回写

## 12. 当前态边界与问题

- CLI/TUI/QQBot 已通过 `IGameSession` + `ISessionGameApi` 收敛为统一门面
- `Program.cs` 仍同时承担装配、初始化、加载和交互分支
- 玩家主线程、角色内部 backchannel、任务线程仍未正式隔离
- `GameBridge` 的 switch 路由已替换为 `[GameApi]` 属性 + `GameApiDispatcher` 属性驱动 dispatch，大幅降低了新增 API 的成本和维护复杂度
- 旧背景文件仍承担过多职责，后续应收缩为角色原型定义，并把系统特化迁移到插件配置

## 13. 目标态架构原则

表现层不直接依赖 `IGameEngine` 或 `IAgentRuntimeHub`。此原则已通过 `IGameSession` 门面实现。

目标态原则：

- 表现层只连接统一门面 `IGameSession`（命令/对话）+ `IGameSessionCatalog`（游戏管理）
- 会话生命周期统一管理
- 输入是结构化 `SessionInput`
- 输出是结构化 `GameEvent`
- CLI/TUI/QQBot/HTTP 都是 adapter，不是业务参与者
- 仍保持单进程，避免不必要的多进程序列化成本

## 14. 目标态分层

目标态图 [target-session-runtime-architecture.dot](/home/yoitsuholo/Code/openLuo-cli/design/technical/target-session-runtime-architecture.dot) 采用明确分层：

- Presentation Adapters
- Session Runtime
- Application Services
- Domain / Engine
- Persistence

这张图的重点不是“当前事实”，而是“未来正确边界”。

## 15. 目标态模块变化

已新增应用层模块：

- `Modules/SessionRuntime`

它位于表现层和现有业务模块之间，负责：

- 会话创建与销毁
- 输入路由
- 输出事件分发
- 启动和恢复流程收口
- runtime 预热收口
- 为前端提供 `ISessionGameApi`（session 级 API，自动注入 gameId）

这意味着当前表现层依赖图已从：

`CLI/TUI -> IGameEngine + IAgentRuntimeHub + 其他高层接口`

收敛成：

`CLI/TUI/QQBot -> IGameSession (+ IGameSessionCatalog 管理游戏)`

## 16. 目标态收益

当前已获得的收益：

- CLI/TUI/QQBot 已通过 `IGameSession` 门面接入
- 前端通过 `session.Api.*` 查询数据时无需传 gameId（`SessionScopedGameApi` 自动解析）
- HTTP / WebSocket 可以自然接入
- 多输入源在同一进程内共享统一会话和输出模型
- 调试、任务、trace、玩家可见输出可以按频道和可见性分离
- `Program.cs` 不再继续膨胀成新的耦合中心

后续可继续推进的收益：

- 新增 GUI 不需要直接理解业务内核模块边界
- 插件 `game/*` API 通过 `[GameApi]` 属性声明即可被 dispatch，无需手写 switch case
