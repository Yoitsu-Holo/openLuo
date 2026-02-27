# 架构分析

## 1. 代码目录映射

### 宿主与入口

- `openLuo/CLI/Program.cs`
- `openLuo/TUI/TuiApplication.cs`
- `openLuo/Modules/AppShell/Application/ServiceCollectionExtensions.cs`

### 业务模块

- `openLuo/Modules/Gameplay`
- `openLuo/Modules/Agent`
- `openLuo/Modules/AgentCapabilities`
- `openLuo/Modules/Executor`
- `openLuo/Modules/InterAgent`
- `openLuo/Modules/WorldState`
- `openLuo/Modules/Assets`
- `openLuo/Modules/Memory`
- `openLuo/Modules/PluginRuntime`
- `openLuo/Modules/GameBridge`
- `openLuo/Modules/Interaction`
- `openLuo/Modules/Content`
- `openLuo/Modules/Llm`

### 内容与资源

- `openLuo/data/archetypes`
- `openLuo/data/item-packs`
- `openLuo/data/plugins`
- `openLuo/data/skills`
- `openLuo/data/tools`
- `openLuo/data/subagents`

### 测试

- `openLuo.Tests/Application`
- `openLuo.Tests/Agent`
- `openLuo.Tests/AgentCapabilities`
- `openLuo.Tests/Executor`
- `openLuo.Tests/Infrastructure`
- `openLuo.Tests/Integration`
- `openLuo.Tests/InterAgent`
- `openLuo.Tests/Security`

## 2. 现阶段最重要的设计事实

- 角色并不是“调用时临时构造”的；游戏启动后可提前预热整个 party 的 runtime。
- Companion 命令与插件命令已经汇合到同一个命令入口，但职责仍然分层。
- Agent 能力已经统一注册，不再依赖单一插件目录去决定能力面。
- 角色人格不写死在 kernel prompt，而是通过预加载 skill 注入。
- 插件不仅能暴露命令，也能通过 hooks 影响上下文、状态和叙事。
- 世界状态不再只是 `game_state` 一张表，而是有 `state_defs + state_store + timeline + time mode` 体系。

## 3. 当前稳定区

- CLI/TUI 双入口
- 数据库初始化与迁移
- 插件发现与工具注册
- State / Timeline / Asset / Memory 基线
- Character turn pipeline
- `/chat`、`/task`、`/switch`
- Inter-agent `ask_character` 与 session chat
- 自动化测试基线

## 4. 当前复杂区

### Agent 调度链较长

链路大致为：

`GameEngine -> AgentInvocationRouter -> CompanionOrchestrator / PluginCommandBridge -> AgentRuntimeHub -> DefaultAgentMessageHandler -> CharacterAgent -> CharacterTurnCoordinator -> Character*Stage`

这条链仍是系统能力最强的部分，也是维护成本最高的部分。旧 `AgentReasoner` 主链已经删除，后续复杂度应继续收敛到 `Executor`、`Memory`、`LLM` 等底层模块，而不是重新堆回 Agent 层。

### GameBridge 仍是宽接口

`game/session/*`、`game/state/*`、`game/asset/*`、`game/timeline/*`、`game/commands/*` 等接口目前都经由统一 mediator 汇合，扩展方便，但收口仍然偏重。

旧 `game/narrative/*`、`game/llm/*`、`game/agent/plan`、`game/give` 已删除，这说明 `GameBridge` 的问题不只是“接口多”，而是曾经混入了第二套 AI 主链。

### 角色定义与系统特化仍未完全拆开

当前旧背景文件仍承载：

- 角色基础资料
- 人格提示词
- 初始地点
- 叙事提示
- 触发器
- 示例台词

这说明旧内容模型仍然耦合较重。  
新方向应是：

- 角色卡只保留角色原型定义
- mood / relationship / daily 等系统特化下沉到插件默认配置与角色覆盖配置
- 通过 schema 与 registry 编译层完成装配
- 宿主 C# 只持有最小 canonical schema 与 bootstrap 规则，扩展逻辑继续留在 Python / MCP 一侧

## 5. 推荐的继续演进方向

- 继续做线程隔离，让玩家主对话、内部角色会话和任务线程分开存储。
- 逐步收窄 `GameBridge`。
- 为角色定义、插件配置、世界状态与扩展包引入更清晰的版本治理和 schema 校验。
- 把“当前有哪些 builtin 能力”自动导出到设计文档或开发工具中，减少文档漂移。
