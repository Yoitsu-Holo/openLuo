# 统一会话运行时实施路线图

本路线图服务于 [unified-session-runtime-architecture.md](./unified-session-runtime-architecture.md) 的落地。

## 状态更新（2026-06）

`SessionRuntime` 基础骨架已经落地，以下阶段不应再按”从零创建 SessionRuntime”的语气理解。

截至 2026-06，已完成项：

- `IGameSession` / `IGameSessionRuntime` / `IGameSessionCatalog` 已实现
- `SessionInput` / `GameEvent` 已落地
- CLI/TUI/QQBot 已通过 `IGameSession` 门面接入
- `ISessionGameApi` / `SessionScopedGameApi`（21 方法，gameId 自动注入）已实现
- `[GameApi]` 属性驱动路由 + `GameApiDispatcher`（44 routes, 10 handlers）已实现

实际剩余重点是：

- 丰富 `GameEvent` 输出模型
- 从 `CommandResult.Output(string)` 迁移到显式表现输出
- 为宿主补上资产读取与媒体投递链

多媒体输出的具体方案见：

- [../technical/multimedia-output-architecture.md](../technical/multimedia-output-architecture.md)

目标不是最小改动，而是把项目推进到长期最优的单进程、多输入源、统一门面架构。

## 1. 总目标

将当前：

- `Program` 直接装配并驱动 CLI/TUI
- CLI/TUI 直接依赖 `IGameEngine`、`IAgentRuntimeHub`
- 输出以终端文本为主

演进为：

- 表现层只依赖 `IGameSessionRuntime`
- 所有输入通过 `SessionInput`
- 所有输出通过 `GameEvent`
- CLI/TUI/GUI/HTTP/WS 都是 adapter

## 2. 实施阶段

### Phase 0: 架构冻结与边界定义

目标：

- 冻结目标架构术语
- 明确新层职责
- 防止后续又回到“入口层直连业务模块”

交付物：

- `unified-session-runtime-architecture.md`
- 本路线图
- `technical/` 下的目标态架构图与说明

验收标准：

- 团队在文档层面对 `SessionRuntime`、`SessionInput`、`GameEvent`、`Adapter` 达成一致

### Phase 1: 新应用层骨架 ✅ 已完成

目标：

- 新增 `Modules/SessionRuntime`
- 先建立接口和空实现，不急于一次替换旧入口

新增内容建议：

- `IGameSessionRuntime`
- `ISessionRegistry`
- `IInputRouter`
- `IOutputEventBus`
- `ISessionInitializationService`
- `SessionOpenRequest`
- `SessionInput`
- `GameEvent`

验收标准：

- 新模块可注册进 DI ✅
- 新接口和模型已稳定 ✅
- CLI/TUI 已通过 IGameSession 门面接入 ✅

额外完成（2026-06）：
- `IGameSession` / `IGameSessionCatalog` per-client session 管理 ✅
- `ISessionGameApi` / `SessionScopedGameApi`（21 方法，gameId 自动注入）✅
- `[GameApi]` 属性驱动路由 + `GameApiDispatcher`（44 routes, 10 handlers）✅

### Phase 2: 生命周期收口 ✅ 已完成

目标：

- 把 `Program.cs` 里的启动、恢复、新建游戏、插件加载、runtime 预热逻辑移入新应用层

需要迁移的能力：

- 配置后初始化流程
- 数据库初始化协调
- 插件加载协调
- 存档探测
- 新游戏初始化
- `EnsurePartyStartedAsync`

新增建议：

- `IGameLifecycleService`
- `ISessionInitializationService`

验收标准：

- CLI/TUI 不再决定何时预热 Agent runtime ✅
- 新游戏初始化流程不再写在 CLI/TUI 内部 ✅

### Phase 3: 输入模型统一 ✅ 已完成

目标：

- 让表现层不再直接提交“裸字符串”
- 全部改走 `SessionInput`

改造范围：

- CLI 行输入
- TUI 输入框提交
- 命令确认响应
- 背景选择
- 新游戏初始化参数提交

关键变化：

- 文本输入只是 `SessionInputKind.Text`
- 确认、选择、结构化动作也成为标准输入类型

验收标准：

- CLI/TUI 都通过 `IGameSession.SubmitAsync(...)` 提交输入 ✅
- 表现层不再直接调用 `IGameEngine.ExecuteAsync(...)` ✅

### Phase 4: 输出模型统一

目标：

- 弱化 `CommandResult.Output/Error` 作为前端边界的作用
- 建立统一事件输出

建议事件类型：

- `TextOutputEvent`
- `NarrativeOutputEvent`
- `ErrorEvent`
- `ConfirmationRequestEvent`
- `StatusSnapshotEvent`
- `TaskProgressEvent`
- `SessionStateEvent`
- `TraceEvent`

关键改造：

- Application Runtime 把内部执行结果映射成 `GameEvent`
- CLI/TUI 订阅事件流而不是读取拼接字符串

验收标准：

- CLI/TUI 只消费 `GameEvent`
- 文本输出只是事件的一种表现形式

### Phase 5: CLI/TUI 适配器化 ✅ 已完成

目标：

- 把当前 CLI/TUI 收缩成真正的 adapter

CLI Adapter 职责：

- 读取 stdin
- 转成 `SessionInput`
- 消费 `GameEvent`
- 打印 stdout/stderr

TUI Adapter 职责：

- 监听 UI 事件
- 转成 `SessionInput`
- 消费 `GameEvent`
- 更新视图组件

验收标准：

- `Program.cs` 不再直接拿 `IGameEngine` 和 `IAgentRuntimeHub` ✅
- `TuiApplication` 不再直接依赖这些内部模块接口 ✅
- QQBot 已通过 `IGameSession` 门面接入 ✅

### Phase 6: 内部事件化与频道化

目标：

- 引入 `Session / Channel / Visibility`
- 为未来多源接入和调试能力做准备

新增能力：

- `main` / `system` / `debug` / `task` / `agent-trace` 频道
- 事件可见性控制
- 同 session 多订阅者支持

验收标准：

- 能区分玩家可见输出和调试输出
- TUI/GUI/HTTP 可以按频道选择订阅

### Phase 7: 新表现层接入验证

目标：

- 用一个新 adapter 证明架构成立

推荐优先顺序：

1. HTTP Adapter
2. GUI Adapter

验收标准：

- 新表现层接入时不需要直接依赖 `IGameEngine`、`IAgentRuntimeHub`
- 新表现层只依赖 `IGameSessionRuntime`

## 3. 模块与文件层面的目标变化

### 现有入口

当前：

- `CLI/Program.cs`
- `TUI/TuiApplication.cs`

目标：

- `Program.cs` 只负责选择 adapter 并启动 session runtime
- `TuiApplication` 仅作为 UI adapter

### 新模块

新增：

- `Modules/SessionRuntime/`

建议结构：

- `Core/Interfaces/IGameSessionRuntime.cs`
- `Core/Interfaces/ISessionRegistry.cs`
- `Core/Interfaces/IInputRouter.cs`
- `Core/Interfaces/IOutputEventBus.cs`
- `Core/Interfaces/ISessionInitializationService.cs`
- `Core/Models/SessionOpenRequest.cs`
- `Core/Models/SessionInput.cs`
- `Core/Models/GameEvent.cs`
- `Application/GameSessionRuntime.cs`
- `Application/SessionRegistry.cs`
- `Application/InputRouter.cs`
- `Application/OutputEventBus.cs`
- `Application/SessionInitializationService.cs`

### 新适配器目录

建议新增：

- `Adapters/Cli/`
- `Adapters/Tui/`
- 未来 `Adapters/Http/`
- 未来 `Adapters/Gui/`

## 4. 风险点

### 4.1 一次性替换过多

风险：

- 容易把启动、输入、输出、任务、确认一起改炸

控制方式：

- 先落接口和 runtime 层
- 再迁生命周期
- 再迁输入
- 最后迁输出与 adapter

### 4.2 输出模型设计过弱

风险：

- 如果只是在 `CommandResult` 外面套一层文本事件，GUI 价值不大

控制方式：

- 一开始就定义结构化事件，而不是继续复制终端文本边界

### 4.3 CLI/TUI 适配器化不彻底

风险：

- 名字换了，实质还是直接调用内核模块

控制方式：

- 以编译依赖为验收标准
- 让表现层项目只能引用 `SessionRuntime` 高层接口

## 5. 架构完成后的判定标准

当下面条件同时满足时，可认定迁移完成：

- CLI/TUI 不再直接依赖 `IGameEngine`
- CLI/TUI 不再直接依赖 `IAgentRuntimeHub`
- 所有表现层只依赖 `IGameSessionRuntime`
- 所有输入都通过 `SessionInput`
- 所有输出都通过 `GameEvent`
- 新增 HTTP/GUI adapter 不需要改动内核模块边界

## 6. 推荐优先级

如果资源有限，建议优先顺序是：

1. `SessionRuntime` 模块骨架
2. 生命周期收口
3. CLI/TUI 改为单门面输入
4. 输出事件化
5. 频道化
6. HTTP/GUI 接入验证

## 7. 结论

这份路线图的重点不是“渐进式最省事”，而是“按正确边界推进”。

如果严格按这个路线执行，最终会得到一套真正适合：

- 单进程
- 多输入源
- 多表现层
- 多会话/多频道
- 长期扩展

的应用运行时架构。
