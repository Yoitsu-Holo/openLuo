# 统一会话运行时架构设计

## 状态更新（2026-05）

这份文档的目标方向仍然有效，但其中部分“当前架构问题”已经过时。

当前代码已经具备：

- `Modules/SessionRuntime`
- `IGameSessionRuntime`
- `IGameSessionCatalog`
- `IGameSession`
- `SessionInput`
- `GameEvent`
- CLI / TUI / QQbot 通过统一门面接入

因此，本文中关于“SessionRuntime 尚未存在”或“CLI/TUI 仍直接驱动核心模块”的描述，应理解为历史背景而不是当前状态。

截至 2026-05，真正尚未完成的关键缺口主要是：

- 富媒体输入理解仍未真正进入 Agent 主链；当前已支持附件接入与结构化输入语义，但多媒体理解仍以“文本指令 + 附件”模式为主
- `GameSessionRuntime` 仍保留 `TextOutputEvent` 兼容层，但结构化输出已经具备 `Visibility`
- 宿主侧最重的耦合已开始收口：
  - `QQbot` 通过 `Ambient` 输入进入上下文，不再直写 `IAgentContextStore`
  - `TUI` 通过 `IGameSessionRuntime` 的角色列表 / 状态快照 / 角色切换接口工作
  - 具体插件命令名仍然保持动态注册，不编码进 enum

另一个已经进入落地阶段的关键变化是：

- `SessionRuntime` 已经从“session 打开后读取默认当前游戏”转向“session 显式绑定 gameId”
- 对外接口层已经收敛成 `IGameSessionCatalog -> IGameSession`
- CLI / TUI 当前采用“默认打开排序后的第一个 gameId”
- QQbot 当前采用“按群号 / 好友号稳定映射到不同 gameId，并为每个目标持有独立 session”

因此，本文中所有“会话”相关设计，今后都应理解为：

- `session` 是绑定某个 `gameId` 的运行时对象
- `sessionId` 是该运行时对象的底层标识
- `gameId` 是存档 / 世界状态标识
- `session` 与 `gameId` 一对一绑定，但概念上不等同

多媒体输出的后续设计请参考：

- [multimedia-output-architecture.md](../technical/multimedia-output-architecture.md)

## 1. 目标

为 `openLuo` 设计一套单进程、强解耦、可长期扩展的表现层接入架构，使：

- CLI
- TUI
- 未来 GUI
- HTTP API
- WebSocket / SSE
- Bot / 自动化控制源

都只通过一个统一的应用门面与游戏内核交互。

这份设计追求的是长期最优架构，而不是最小改动。

## 2. 核心结论

最佳方案是：

**单进程 + 统一应用门面 + 多输入适配器 + 统一事件输出总线 + 会话/频道模型**

不采用：

- 多进程前后端拆分
- CLI/TUI/GUI 各自直连 `IGameEngine`、`IAgentRuntimeHub`
- 以字符串输入和纯文本输出作为唯一边界

## 3. 当前架构问题

当前代码已经有一定的高层接口抽象，但还不够。

### 3.1 表现层直接碰多个内部模块

说明：

这部分在当前代码中已经显著改善。CLI / TUI / QQbot 已主要通过 `IGameSessionRuntime` 接入。

但“统一门面已存在”不等于“输出边界已经足够稳定”，富媒体输出仍需继续收口。

当前 CLI / TUI 都直接接触：

- `IGameEngine`
- `IAgentRuntimeHub`
- `IGameStreams`
- `IReadOnlyList<SessionArchetypeOption>`

这意味着：

- 表现层知道太多初始化细节
- 表现层知道何时要预热 agent runtime
- 表现层知道新游戏初始化需要哪些数据

### 3.2 启动流程没有收口

`Program.cs` 目前同时承担：

- 配置读取
- DI 装配
- 数据库初始化
- 插件加载
- 存档探测
- 新游戏初始化
- CLI 交互循环
- TUI 启动分支

这导致：

- 新接入一种表现层时，仍需继续修改入口文件
- 启动/恢复/初始化流程难以复用

### 3.3 输出模型过于终端化

当前主要输出对象还是：

- `CommandResult.Success`
- `CommandResult.Output`
- `CommandResult.Error`
- `TextOutputEvent`

这对 CLI 足够，但对 GUI / HTTP / WebSocket 不够。

因为这些表现层需要的是：

- 结构化消息
- 状态更新事件
- 任务进度事件
- 角色消息事件
- 系统提示事件

### 3.4 输入来源没有统一模型

当前默认输入是“终端中的一行文本”。

未来如果加入：

- HTTP 请求
- GUI 按钮
- WebSocket 消息
- 自动脚本

就会出现“输入来源多样，但系统内部没有统一输入信封”的问题。

## 4. 目标架构原则

### 4.1 表现层只连一个模块

CLI / TUI / GUI / HTTP 不直接碰内部业务模块。

它们只能依赖一个统一门面，例如：

- `IGameSessionRuntime`

### 4.2 内核与表现层通过结构化协议交互

输入不是裸字符串，输出不是纯文本拼接。

应该采用：

- `SessionInput`
- `GameEvent`

作为统一边界协议。

### 4.3 单进程，不牺牲内聚性

所有模块仍运行在一个宿主进程内：

- 最低延迟
- 最低序列化成本
- 最方便共享内存对象和数据库连接管理

### 4.4 多入口只是适配器，不是业务参与者

CLI/TUI/GUI/HTTP 的职责应仅为：

- 接收输入
- 转成 `SessionInput`
- 提交到统一门面
- 订阅或接收 `GameEvent`
- 渲染输出

### 4.5 会话与频道是一等公民

未来不同输入源不只是“来源不同”，交互语义也不同。

因此系统需要：

- `Session`
- `Channel`
- `Audience`
- `Visibility`

这些概念。

## 5. 推荐总体结构

建议新增一个明确的 `Application Runtime` 层，位于表现层与现有模块之间。

推荐分层：

### Presentation Adapters

- CLI Adapter
- TUI Adapter
- GUI Adapter
- HTTP Adapter
- WebSocket Adapter

### Application Runtime

- `IGameSessionRuntime`
- `ISessionRegistry`
- `IInputRouter`
- `IOutputEventBus`
- `IGameLifecycleService`
- `ISessionInitializationService`

### Domain / Engine

- Gameplay
- Agent
- Executor
- AgentCapabilities
- InterAgent
- PluginRuntime
- GameBridge
- WorldState
- Assets
- Memory
- Llm

## 6. 核心接口设计

### 6.1 统一会话门面

```csharp
public interface IGameSessionRuntime
{
    Task<SessionHandle> OpenAsync(SessionOpenRequest request, CancellationToken ct = default);
    Task CloseAsync(string sessionId, CancellationToken ct = default);
    Task SubmitAsync(SessionInput input, CancellationToken ct = default);
    IAsyncEnumerable<GameEvent> StreamEventsAsync(string sessionId, CancellationToken ct = default);
}
```

它是所有表现层唯一允许接触的高层接口。

### 6.2 会话打开请求

```csharp
public sealed class SessionOpenRequest
{
    public required string ClientType { get; init; }     // cli / tui / gui / http / ws
    public required string ClientId { get; init; }
    public string? RequestedGameId { get; init; }
    public string? RequestedSaveSlot { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}
```

### 6.3 统一输入模型

```csharp
public sealed class SessionInput
{
    public required string SessionId { get; init; }
    public required string SourceId { get; init; }       // cli, tui, gui, http
    public required string ChannelId { get; init; }      // main, task, admin, system
    public required string ActorId { get; init; }        // player, user:<id>, system
    public required SessionInputKind Kind { get; init; } // Text, Chat, Command, Ambient, System, Selection, Confirm
    public string? Text { get; init; }
    public SessionCommandInvocation? Command { get; init; }
    public SessionInputOrigin? Origin { get; init; }
    public SessionPresentationProfile PresentationProfile { get; init; }
    public Dictionary<string, string> Arguments { get; init; } = new();
    public Dictionary<string, string> Metadata { get; init; } = new();
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

补充说明：

- `SessionInputKind` 是稳定的顶层输入语义，不是具体插件命令枚举
- 具体命令名例如 `switch`、`chat`、插件新增的 `foo/bar` 等，必须通过 `SessionCommandInvocation.Name` 和现有动态命令注册体系解析
- 这样既能让宿主表达结构化意图，又不会破坏插件动态扩展命令系统

### 6.4 统一事件模型

```csharp
public abstract class GameEvent
{
    public required string SessionId { get; init; }
    public required string ChannelId { get; init; }
    public required string EventId { get; init; }
    public required GameEventKind Kind { get; init; }
    public EventVisibility Visibility { get; init; } = EventVisibility.ClientVisible;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

事件类型建议至少包括：

- `TextOutputEvent`
- `MessageOutputEvent`
- `RichNarrativeEvent`
- `ErrorEvent`
- `StatusSnapshotEvent`
- `PromptRequestEvent`
- `ConfirmationRequestEvent`
- `TaskProgressEvent`
- `AgentTraceEvent`
- `SessionStateEvent`
- `SystemNoticeEvent`

## 7. 新层的职责分配

### 7.1 IGameSessionRuntime

负责：

- 会话创建和销毁
- 会话事件流建立
- 输入提交总入口
- 与 session registry、input router、event bus 协作

不负责：

- 具体玩法
- 具体 UI 渲染

### 7.2 ISessionRegistry

负责：

- 当前有哪些会话
- 每个会话绑定哪个 game/save
- 每个会话有哪些订阅者
- 每个会话当前状态

### 7.3 ISessionInitializationService

负责统一处理：

- 检查是否已有存档
- 创建新游戏
- 恢复已有游戏
- 预热 agent runtime
- 加载背景与启动状态

这样 CLI / TUI / GUI 都不需要再自己做初始化流程。

### 7.4 IInputRouter

负责：

- 把 `SessionInput` 映射到内部命令调用
- 区分文本命令、结构化动作、确认响应
- 决定输入发往哪个内部线程/频道

### 7.5 IOutputEventBus

负责：

- 从内部模块接收事件
- 按 session / channel / visibility 分发
- 提供订阅接口给 CLI / TUI / GUI / HTTP

## 8. 线程与频道模型

为了支持多种控制源和更复杂的交互，建议显式引入频道。

### 8.1 Session

一个会话表示一个连接到游戏进程的前端上下文。

例如：

- 一个 CLI 终端
- 一个 TUI 窗口
- 一个 GUI 客户端
- 一个 HTTP 长连接控制台

### 8.2 Channel

一个会话中可以有多个逻辑频道：

- `main`
- `party-task`
- `system`
- `debug`
- `agent-trace`

### 8.3 Visibility

每个事件还要声明可见性：

- `ClientVisible`
- `SessionPrivate`
- `DebugOnly`
- `SystemOnly`

这样以后：

- GUI 可以不显示 trace
- CLI 可以显示 debug
- HTTP API 可以只返回 client visible 事件

## 9. 内部模块如何接入新门面

### 9.1 Gameplay

`IGameEngine` 不再由表现层直接调用，而由 `IInputRouter` 调用。

### 9.2 AgentRuntimeHub

不再由 CLI/TUI 直接调用，而是在：

- `ISessionInitializationService`
- 会话恢复流程
- 需要时的系统生命周期控制

中统一调用。

### 9.3 GameBridge / PluginRuntime

仍然保留现有架构，但它们的输出要允许转成结构化 `GameEvent`。

### 9.4 IGameStreams

不再作为表现层主抽象。

建议把它降级为：

- 某些控制源适配器的兼容层
- 或宿主内部少量桥接用途

长期应该由 `IOutputEventBus` 取代其在表现层边界中的核心地位。

## 10. 表现层适配器的推荐模式

### 10.1 CLI Adapter

负责：

- 读取 stdin 文本
- 转成 `SessionInput`
- 监听 `GameEvent`
- 打印到 stdout/stderr

### 10.2 TUI Adapter

负责：

- 把按键、输入框、按钮操作转成 `SessionInput`
- 监听事件流并更新 UI 区域

### 10.3 GUI Adapter

负责：

- 把按钮点击、输入框提交、菜单操作转成 `SessionInput`
- 按事件类型更新聊天区、状态栏、任务区、日志区

### 10.4 HTTP Adapter

负责：

- 把 HTTP 请求映射成 `SessionInput`
- 把同步响应或事件快照映射成 JSON

### 10.5 WebSocket / SSE Adapter

负责：

- 维持长连接
- 推送 `GameEvent`
- 接收实时控制消息

## 11. 为什么这是最优方案

### 11.1 对新增表现层最友好

新增 GUI、HTTP、Bot 时：

- 不需要知道 `IGameEngine`
- 不需要知道 `IAgentRuntimeHub`
- 不需要知道如何初始化新游戏
- 不需要知道如何预热 runtime

只需要实现 adapter。

### 11.2 对内部模块侵入最小且边界更清晰

虽然整体重构量不小，但对长期边界最干净：

- 表现层不再污染业务层
- 初始化/恢复/生命周期逻辑不再分散
- 输出模型不再被终端文本绑死

### 11.3 对复杂交互更稳

未来如果出现：

- GUI 与 CLI 同时连接同一个游戏会话
- HTTP 控制 + TUI 观察
- Debug 频道与玩家频道分离

这套设计能自然支持。

## 12. 明确不建议的替代方案

### 12.1 每个表现层直接接 `IGameEngine`

缺点：

- 初始化流程重复
- 表现层知道太多业务细节
- 加 GUI / HTTP 会越来越乱

### 12.2 纯文本输入输出边界

缺点：

- GUI/HTTP 难以获得结构化结果
- trace、状态、任务、确认、提示都混在一条文本里

### 12.3 多进程内核拆分

对当前项目不是最优。

原因：

- 增加序列化成本
- 增加部署复杂度
- 增加故障面
- 当前问题本质上不是“是否单进程”，而是“是否有统一门面”

## 13. 推荐模块落位

建议新增：

- `openLuo/Modules/SessionRuntime/`

包含：

- `Core/Interfaces/IGameSessionRuntime.cs`
- `Core/Models/SessionInput.cs`
- `Core/Models/GameEvent.cs`
- `Application/GameSessionRuntime.cs`
- `Application/SessionRegistry.cs`
- `Application/InputRouter.cs`
- `Application/OutputEventBus.cs`
- `Application/SessionInitializationService.cs`

同时在表现层新增：

- `openLuo/Adapters/Cli/`
- `openLuo/Adapters/Tui/`
- 未来 `openLuo/Adapters/Http/`
- 未来 `openLuo/Adapters/Gui/`

## 14. 推荐迁移终态

理想终态下：

- `Program.cs` 只负责选择和启动 adapter
- 所有 adapter 只依赖 `IGameSessionRuntime`
- `IGameSessionRuntime` 是表现层唯一统一入口
- 所有输出都通过 `GameEvent` 流动
- 现有业务模块继续留在单进程宿主内

## 15. 设计结论

对于你的目标场景：

- 不采用多进程
- 允许多个输入源
- 所有输入仍在一个游戏进程内处理
- 希望未来 GUI/HTTP/TUI/CLI 高度解耦

最优架构就是：

**单进程统一会话运行时架构**

其关键不是 stdio 本身，而是：

- 单一应用门面
- 结构化输入
- 结构化事件输出
- 表现层适配器化
- 会话/频道/可见性模型

这才是长期最稳定、最易扩展、最适合你项目未来演化方向的方案。
