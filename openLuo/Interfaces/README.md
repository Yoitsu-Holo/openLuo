# Interfaces

`Interfaces` 存放 openLuo 面向用户和外部平台暴露的运行方案。

这些运行方案可以拥有自己的产品交互逻辑，但它们应该通过 `SessionRuntime` 这一层门面访问
`GameKernel`，而不是直接伸手进入 Agent、Gameplay、存储层等内部模块。

## 运行方案

- `CLI`：轻量命令行运行方案，负责提交文本输入并渲染返回事件。
- `TUI`：终端 UI 运行方案，负责更完整的交互流程，例如初始化、角色列表、状态面板和角色选择。
- `QQbot`：基于 Milky 的即时通讯运行方案，负责 QQ 侧传输、@ 路由、群聊/好友场景和平台消息投递。

这些运行方案不要求是“薄适配器”。它们是 openLuo 当前对外提供的用户入口，可以承担一部分应用层编排。
边界要求是：内核行为必须通过公开 runtime 接口表达，而不是由宿主直接修改内核内部状态。

## GameKernel 主门面

当前对外接口已经收敛为两层：

- `IGameSessionCatalog`
- `IGameSession`

其中：

- `IGameSessionCatalog` 负责“列出存档 / 打开会话”
- `IGameSession` 负责“绑定某个存档后的全部操作”

也就是说，宿主现在的目标模型是：

1. 先通过 catalog 获取 `gameId` 列表
2. 再打开一个绑定该 `gameId` 的 session
3. 后续所有调用只面向 session，而不是每次重新传 `gameId`

### `IGameSessionCatalog`

核心职责包括：

- 存档枚举：`GetGameIdsAsync`
- 原型查询：`GetAvailableArchetypesAsync`
- 打开普通 session：`OpenSessionAsync`
- 直接按 `gameId` 打开 session：`OpenGameSessionAsync`

### `IGameSession`

核心职责包括：

- 事件流：`StreamEventsAsync`
- 输入提交：`SubmitAsync`
- 游戏初始化：`InitGameAsync`
- 状态读取：`TryGetStateAsync`
- 角色列表 / 状态查询：`GetSessionRosterAsync`、`GetCharacterStatusSnapshotAsync`
- 当前激活角色控制：`SetActiveCharacterAsync`
- 附件和资产访问：`GetAttachmentsAsync`、`GetAttachmentAsync`、`GetAssetDescriptorAsync`、`GetAssetBlobAsync`
- 会话关闭：`CloseAsync`

`IGameSessionRuntime` 仍然保留在内部实现层，作为底层 runtime 门面存在；但宿主侧不应继续直接以
`runtime + sessionId` 形式工作。

## 输入契约

所有宿主都通过 `GameSessionInput` / `SessionInput` 提交输入。

稳定的输入语义由 `SessionInputKind` 表达：

- `Text`：兼容用的原始文本输入，通常仍会被解析成命令文本。
- `Chat`：玩家对角色的直接聊天输入。runtime 会将它映射到内部 chat 路径。
- `Command`：结构化命令调用。具体命令名保持动态。
- `Ambient`：只进入上下文、不触发回复的环境输入，例如群聊中未 @ 机器人的普通消息。
- `System`：宿主或系统侧输入。
- `Selection` / `Confirm`：预留的交互原语。

具体命令由 `SessionCommandInvocation` 表达，而不是由 enum 表达。这一点是有意设计：
插件命令是动态注册的，所以 `foo`、`bar` 这类命令名必须保持数据驱动。

输入来源信息应放在 `SessionInputOrigin` 中：

- 平台：`cli`、`tui`、`qq`、`gui` 等。
- 场景：`main`、`group`、`friend` 等。
- 会话和用户身份。
- 是否为私聊、是否 @ 了机器人。

展示意图使用 `SessionPresentationProfile` 表达：

- `Default`
- `Narrative`
- `InstantMessageCompact`
- `RichDesktop`

这样宿主可以请求某种回复风格，而不需要根据宿主身份直接注入 prompt。

## 输出契约

所有宿主都消费 `GameEvent`。

主要事件类型包括：

- `InputAcceptedEvent`：runtime 已接受并路由一次输入。
- `MessageOutputEvent`：结构化输出消息，包含文本和资产片段。
- `TextOutputEvent`：兼容用文本输出。
- `AttachmentAcceptedEvent`：runtime 已接收一个输入附件。
- `StatusSnapshotEvent`：会话状态投影。
- `SystemNoticeEvent`：运行时提示或诊断通知。
- `ErrorEvent`：运行时错误。

结构化消息片段包括：

- `TextMessageOutputPart`
- `AssetMessageOutputPart`

媒体内容通过 `assetId`、`mimeType` 和 `blobRole` 引用。支持媒体渲染的宿主应通过
`IGameSession.GetAssetBlobAsync` 获取字节，而不是直接依赖资产存储内部实现。

输出可见性由 `OutputVisibility` 表达：

- `Public`：正常面向用户的内容。
- `StateSummary`：状态结算或状态 bookkeeping。
- `System`：runtime 或系统提示。
- `Debug`：诊断和 trace 内容。

宿主应根据 visibility 路由或过滤输出，而不是通过解析文本前缀判断内容语义。

## 当前边界规则

- 宿主代码可以拥有交互流程、平台路由和渲染策略。
- 宿主代码不应直接写 `IAgentContextStore`。
- 宿主代码不应根据宿主身份直接注入 prompt 文本。
- 宿主代码不应在已有 runtime 方法可用时，通过 `/switch` 这类字符串命令驱动 UI 控制。
- 宿主代码不应通过解析展示文本来推断可见性或语义。

## 当前 runtime 映射

当前实现仍复用了一部分内部文本命令路径：

- `SessionInputKind.Chat` 会映射到内部 `/chat` 路径。
- `SessionInputKind.Command` 会映射到现有动态命令系统。
- `SessionInputKind.Ambient` 会通过 `SessionRuntime` 写入上下文，而不是由宿主直接修改 Agent store。

这种方式保留了插件命令兼容性，同时让外部接口层更干净。

## 当前会话模型

当前架构明确区分两种标识：

- `gameId`：存档 / 世界状态主键
- `session`：绑定到某个 `gameId` 的运行时上下文

它们可以相同，但不应被默认视为同一个概念。

当前宿主策略：

- `CLI` / `TUI`：默认打开排序后的第一个 `gameId`
- `QQbot`：按群号 / 好友号稳定映射到不同 `gameId`，并为每个目标持有独立 session
