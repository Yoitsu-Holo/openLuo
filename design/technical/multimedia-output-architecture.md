# 多媒体输出架构设计

## 1. 目标

为 `openLuo` 增加一条清晰、可扩展、跨宿主一致的多媒体输出主链，使：

- TUI / GUI / QQbot 等多媒体宿主可以接收图片
- 后续可以自然扩展到音频、视频、Live2D 动作、文件卡片等非文本内容
- 文本、媒体引用、二进制 blob 三条数据流职责分离
- `Executor` / `Agent` / `SessionRuntime` / Host Adapter` 的边界保持明确

这份设计关注的是“输出主链”，不是输入附件处理。

## 2. 当前代码现实

当前实现已经具备一部分基础设施，但还没有打通端到端的多媒体输出。

补充说明：

- 当前 runtime 已开始引入显式 `gameId` 绑定模型
- 多媒体资产、状态、记忆、角色上下文等都应继续挂在 `gameId` 维度，而不是宿主 `sessionId`
- 未来无论是 CLI / TUI / QQbot / GUI，媒体输出都应理解为“某个 session 正在呈现某个 gameId 的结果”

### 2.1 已具备：输入附件入库

`SessionInput` 已支持非文本输入片段：

- `SessionContentKind.Text`
- `SessionContentKind.Binary`
- `SessionContentKind.FileReference`

代码位置：

- `openLuo/Modules/SessionRuntime/Core/Models/SessionInput.cs`

`GameSessionRuntime.SubmitAsync(...)` 会把输入附件写入 `IInputContentStore`，随后通过 `AttachmentAssetBridge` 落入资产系统：

- `openLuo/Modules/SessionRuntime/Application/GameSessionRuntime.cs`
- `openLuo/Modules/SessionRuntime/Application/AttachmentAssetBridge.cs`

这条链路解决的是：

- “宿主输入了一个文件/图片”
- “运行时如何接收并入库”

它不是输出链路。

### 2.2 已具备：资产系统可以保存和读取 blob

当前资产系统已经能：

- 建立 `asset record`
- 写入 blob
- 查询 blob 元信息
- 按 `blobId` 读取原始字节

代码位置：

- `openLuo/Modules/Assets/Core/Interfaces/IAssetBlobStore.cs`
- `openLuo/Modules/Assets/Infrastructure/AssetBlobStore.cs`

因此“图片数据存在哪里”不是问题。

### 2.3 已具备：插件 / 外部执行器可以产出资产

现有插件已经展示了“生成资产并入库”的能力，例如：

- `builtin_asset_cg_generator`
- `builtin_asset_bg_generator`
- `builtin_asset_gallery`

它们通过 `game/asset/create`、`game/asset/blob_put`、`game/asset/meta_put` 等 API 写入资产系统。

代码位置：

- `openLuo/data/plugins/builtin_asset_cg_generator/main.py`
- `openLuo/data/plugins/builtin_asset_bg_generator/main.py`
- `openLuo/Modules/GameBridge/Infrastructure/Handlers/AssetApiHandler.cs`

这说明：

- “ComfyUI / 图床 / 资源抓取器” 作为 executor 或 plugin 产出图片资产是完全合理的
- 当前缺的不是资产生成能力，而是“资产如何成为一次会话输出”

### 2.4 当前现实：输出主链已建立，但输入主链仍不对称

截至当前代码：

- `CommandResult -> CommandPresentation -> MessageOutputEvent` 输出主链已经成立
- `QQbot` 已能消费 `image/*` 类型的 `AssetMessageOutputPart`
- `CLI / TUI / QQbot` 都已经开始以结构化消息事件作为前台输出来源
- `TextOutputEvent` 仍然保留为兼容层，而不是唯一出口

因此，输出侧最关键的“媒体引用主链”已经打通，真正尚未完成的是：

- 输入侧仍然没有与输出对称的 message-parts 主链
- Agent 主输入仍然以文本理解为核心
- 多媒体输入当前仍主要表现为“文本指令 + 附件/资产”，而不是原生多模态对话消息

## 3. 设计结论

不需要推翻当前架构重写。

需要做的是：

1. 保留 `Assets` 作为二进制真实载体
2. 在 `CommandResult` 之上新增正式的“表现输出契约”
3. 在 `SessionRuntime` 内新增正式的“富媒体事件输出”
4. 在宿主层消费“媒体引用”，而不是消费 blob 或魔法字符串

也就是说，这不是“QQbot 加发图特判”，而是一条新的稳定输出主链。

## 4. 设计原则

### 4.1 文本、媒体引用、blob 三层分离

必须明确区分：

- 文本内容：角色对白、说明、caption
- 媒体引用：`assetId`、`mimeType`、呈现提示
- 二进制载荷：PNG / WAV / MP4 / motion 文件本体

禁止把这三者混成同一层。

### 4.2 Agent 只决定“表达内容”，不负责媒体传输

Agent 可以决定：

- 回一段话
- 附一张图
- 附一段语音

但 Agent 不负责：

- 管理 blob 字节
- 决定平台 API 细节
- 直接发 QQ / GUI / WebSocket 消息

### 4.3 Executor 只输出公开成立的媒体结果

遵守 `executor-contract-guidelines.md`：

- executor 不输出“仅供下游理解的私有控制字段”
- executor 不通过 `Metadata` 偷塞平台指令
- executor 若生成图片，应返回“资产引用结果”

正确结果：

- `assetId`
- `mimeType`
- `display metadata`

错误结果：

- `please_send_to_qq=true`
- `next_executor_should_attach_this_image`
- `blobBase64` 直接塞到角色上下文

### 4.4 SessionRuntime 负责把执行结果投影成宿主可消费事件

`SessionRuntime` 是宿主门面。

因此：

- `Gameplay / Agent` 不直接生成 QQ 消息段
- `QQbot / TUI / GUI` 不直接理解 executor 结果
- `SessionRuntime` 负责把命令/角色结果投影成统一 `GameEvent`

### 4.5 Host Adapter 负责能力适配和降级

不同宿主可以支持不同能力：

- QQbot：文本 + 图片，音频/视频后续再接
- CLI：只文本，媒体显示成占位提示
- TUI：先做占位和 asset 引用，后续再做图片预览
- GUI：可做原生多媒体展示

宿主只能消费统一事件，不应回头依赖 Agent 或 Asset 内部模块。

## 5. 推荐数据流

### 5.1 输入附件链

```text
Host Input
  -> SessionInput.Parts
  -> InputContentStore
  -> AttachmentAssetBridge
  -> Assets(blob/meta)
```

职责：

- 接收外部上传的媒体
- 归档为 session attachment / imported asset

### 5.2 生成媒体链

```text
Executor / Plugin / Capability
  -> Assets(blob/meta/link)
  -> Media Reference Result
  -> Agent/Gameplay Final Presentation
  -> SessionRuntime MessageOutputEvent
  -> Host Adapter
```

职责：

- 生成或抓取媒体
- 入库为正式资产
- 只把 `assetId` 等引用向上返回

### 5.3 宿主投递链

```text
Host Adapter
  -> IGameSessionRuntime.GetAssetDescriptor / GetAssetBlob
  -> platform-specific delivery
```

职责：

- 按宿主能力取回媒体字节
- 发 QQ 图、显示 GUI 图片、打印 CLI 占位

注意：

- Host 不应直接依赖 `IAssetBlobStore`
- Host 应通过 `SessionRuntime` 取媒体

## 6. 推荐契约设计

## 6.1 `CommandResult` 从“单文本结果”升级为“显式表现结果”

当前 `CommandResult` 仍以 `Output` 文本为中心，这会卡住所有富媒体宿主。

建议保留 `CommandResult`，但新增显式表现字段：

```csharp
public sealed class CommandResult
{
    public bool Success { get; set; } = true;
    public string Output { get; set; } = string.Empty; // 兼容字段
    public string? Error { get; set; }
    public CommandPresentation Presentation { get; set; } = CommandPresentation.Empty;
    public Dictionary<string, object?> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
```

其中 `Metadata` 仍然只允许诊断/内部附加信息，不允许承担媒体主链。

### 6.1.1 新增 `CommandPresentation`

建议放在：

- `openLuo/Modules/Commanding/Core/Models/`

建议模型：

```csharp
public sealed class CommandPresentation
{
    public static CommandPresentation Empty { get; } = new();
    public IReadOnlyList<PresentationMessage> Messages { get; init; } = [];
}

public sealed class PresentationMessage
{
    public required string MessageId { get; init; }
    public string SpeakerRole { get; init; } = "assistant";
    public string? SpeakerId { get; init; }
    public IReadOnlyList<PresentationPart> Parts { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public abstract class PresentationPart
{
    public required PresentationPartKind Kind { get; init; }
}

public sealed class TextPresentationPart : PresentationPart
{
    public required string Text { get; init; }
}

public sealed class AssetPresentationPart : PresentationPart
{
    public required string AssetId { get; init; }
    public required string MimeType { get; init; }
    public string BlobRole { get; init; } = "primary";
    public string? AltText { get; init; }
    public string? Caption { get; init; }
    public string? Name { get; init; }
    public string? RenderHint { get; init; } // inline / card / gallery / voice / motion
}
```

### 6.1.2 兼容策略

第一阶段不要马上删 `Output`。

兼容规则：

- 旧链仍可继续返回 `CommandResult.Ok("纯文本")`
- 新链若只包含一个文本 part，可自动投影回 `Output`
- 新链若包含媒体 part，`Output` 只作为纯文本 fallback，不作为主链

## 6.2 `GameEvent` 新增 `MessageOutputEvent`

当前 `TextOutputEvent` 只能承载字符串，无法表达：

- 图文混排
- 多媒体 part 顺序
- 同一次输出内多个资源

建议在 `SessionRuntime` 层新增目标事件：

```csharp
public sealed class MessageOutputEvent : GameEvent
{
    public required string MessageId { get; init; }
    public string SpeakerRole { get; init; } = "assistant";
    public string? SpeakerId { get; init; }
    public IReadOnlyList<MessageOutputPart> Parts { get; init; } = [];
}
```

`MessageOutputPart` 建议与 `PresentationPart` 对应，但它是 `SessionRuntime` 面向宿主的投影结果，不应带 blob。

建议 part 类型至少包括：

- `Text`
- `Asset`

后续扩展：

- `Action`
- `Choice`
- `StatusCard`

### 6.2.1 `TextOutputEvent` 的迁移策略

不建议第一阶段删除 `TextOutputEvent`。

建议：

- 新链优先发布 `MessageOutputEvent`
- 对于纯文本消息，可继续同步发布 `TextOutputEvent` 兼容旧宿主
- QQbot / TUI / GUI 新代码优先消费 `MessageOutputEvent`

等所有宿主迁移完成后，再考虑弱化 `TextOutputEvent`

## 6.3 `IGameSessionRuntime` 增加“资产读取”门面

宿主适配器不应直接依赖 `Assets` 模块内部接口。

因此建议在 `IGameSessionRuntime` 增加媒体读取能力，例如：

```csharp
Task<SessionAssetDescriptor?> GetAssetDescriptorAsync(
    string sessionId,
    string assetId,
    CancellationToken ct = default);

Task<SessionAssetBlob?> GetAssetBlobAsync(
    string sessionId,
    string assetId,
    string blobRole = "primary",
    CancellationToken ct = default);
```

建议模型：

```csharp
public sealed class SessionAssetDescriptor
{
    public required string AssetId { get; init; }
    public required string AssetType { get; init; }
    public required string Namespace { get; init; }
    public string? Label { get; init; }
    public IReadOnlyList<SessionAssetBlobInfo> BlobInfos { get; init; } = [];
}

public sealed class SessionAssetBlob
{
    public required string AssetId { get; init; }
    public required string BlobId { get; init; }
    public required string MimeType { get; init; }
    public required byte[] Data { get; init; }
}
```

这样：

- QQbot 可以通过 runtime 取图片字节并发图
- GUI 可以通过 runtime 取缩略图或主图
- CLI/TUI 也能统一地显示 asset 信息

## 7. Agent / Executor 侧职责划分

## 7.1 不让 `CharacterResponseExecutor` 直接承担媒体生成

这类 executor 的单一职责仍应是：

- 生成对白文本

不应变成：

- 对白生成 + 图像生成 + 动作生成 + 宿主渲染控制

### 正确方式

媒体应来自独立能力：

- `generate_cg`
- `fetch_reference_image`
- `synthesize_voice`
- `play_motion`

这些能力可以是：

- plugin
- capability
- dedicated executor

它们的结果应是：

- 已入库资产
- 返回 `assetId` / `mimeType` / 语义标签

## 7.2 `Agent` 最终回合结果应允许携带表现消息

当前 `CharacterTurnResult` 仍以 `Reply` 为中心。

建议演进方向：

- 保留 `Reply` 作为纯文本兼容字段
- 新增显式的 `Presentation` 或 `OutputMessages`

例如：

```csharp
public sealed class CharacterTurnResult
{
    public string Reply { get; init; } = string.Empty; // 兼容
    public CharacterPresentation Presentation { get; init; } = CharacterPresentation.Empty;
    ...
}
```

但要注意：

- 这不是让 `CharacterResponseExecutor` 自己发图
- 而是让最终回合汇总器把“文本结果 + 媒体能力结果”拼成表现结果

## 7.3 不允许把媒体结果塞进 `CommandResult.Metadata`

这是本设计最重要的约束之一。

禁止：

- `result.Metadata["assetId"] = ...`
- `result.Metadata["qq.image"] = ...`
- `result.Output = "[image:asset_xxx]"` 然后宿主自己解析

原因：

- 这会重新引入隐式 side-channel
- 会让 QQ / GUI / TUI 各自写一套文本解析 hack
- 会直接违反现有 executor contract 文档

## 8. 宿主适配策略

## 8.1 QQbot

目标：

- 文本 part -> 文本消息段
- 图片 asset part -> 图片消息段
- 同一条输出里允许“@ + 文本 + 图片”

实现建议：

1. `QQbot` 优先消费 `MessageOutputEvent`
2. 遇到 `AssetPresentationPart`：
   - 通过 `IGameSessionRuntime.GetAssetBlobAsync(...)` 取 `image/*` 字节
   - 转 `Milky` 图片出站段
3. 若 part 类型暂不支持：
   - 降级为 `[暂不支持的内容: audio/mp4/... ]`

注意：

- QQ 适配器只做宿主投递，不判断业务语义
- 不从文本里解析“图片标记”

## 8.2 TUI

第一阶段：

- 文本照常显示
- 图片先显示占位卡片：
  - `图片：<label>`
  - `assetId=...`
  - `mimeType=image/png`

第二阶段：

- 再考虑加图片预览或外部查看器集成

## 8.3 CLI

CLI 永远允许简单降级：

```text
[图片] label=夏日海边 assetId=asset_xxx mime=image/png
```

CLI 不需要为多媒体成为一等渲染器，但必须能正确显示事件边界。

## 8.4 GUI

GUI 未来应直接以 `MessageOutputEvent` 为主协议：

- 文本 part -> 富文本组件
- 图片 part -> 图片组件
- 音频 part -> 播放器组件
- motion part -> Live2D 触发器

## 9. 阶段性落地建议

### Phase 1：补正式契约，不接业务生成

目标：

- `CommandResult.Presentation`
- `MessageOutputEvent`
- `IGameSessionRuntime.GetAssetDescriptorAsync / GetAssetBlobAsync`

此阶段不要求角色真的会发图，只先把主链打通。

### Phase 2：QQbot 图片发送最小闭环

目标：

- `QQbot` 消费 `MessageOutputEvent`
- 先支持 `Text + Image`
- TUI / CLI 先做占位降级

这一步完成后，你就可以验证：

- 资产系统中的一张图是否能被 QQ 真正发出去

### Phase 3：Agent/Capability 接入图片输出

目标：

- 新 executor / capability 产出 `assetId`
- 最终角色回复支持“文本 + 图片”

建议优先做：

- `random_image_fetch`
- `cg_generate`

### Phase 4：扩展到音频 / 视频 / 动作

此时不需要再改主链，只是新增 part 类型与宿主能力。

## 10. 明确不做的事情

本设计明确不采用以下方案：

### 10.1 不把 blob 放进 Agent 上下文

不允许：

- base64 图片进入上下文
- 音频字节进入 memory / prompt

### 10.2 不把媒体协议藏进文本标记

不允许：

- `[IMAGE:asset_xxx]`
- `<qq-image asset="...">`
- 依赖宿主解析 `Output` 特殊字符串

### 10.3 不让 QQbot 成为第一责任人

QQbot 只是第一个验证宿主，不是架构中心。

多媒体主链必须成立于：

- `Commanding`
- `Agent`
- `SessionRuntime`

然后才是：

- `QQbot`
- `TUI`
- `GUI`

## 11. 推荐改造文件范围

第一阶段建议触及：

- `openLuo/Modules/Commanding/Core/Models/Command.cs`
- `openLuo/Core/Interfaces/IGameEngine.cs`
- `openLuo/Modules/Agent/Application/Chat/PlayerChatDispatcher.cs`
- `openLuo/Modules/Agent/Application/.../Character*`
- `openLuo/Modules/SessionRuntime/Core/Models/GameEvent.cs`
- `openLuo/Modules/SessionRuntime/Core/Interfaces/IGameSessionRuntime.cs`
- `openLuo/Modules/SessionRuntime/Application/GameSessionRuntime.cs`
- `openLuo/Interfaces/QQbot/QqBotApplication.cs`
- `openLuo/Interfaces/TUI/TuiApplication.cs`
- `openLuo/Interfaces/CLI/CliApplication.cs`

## 12. 最终建议

对当前项目，最合理的路线不是“大改重做”，而是：

1. 把“媒体输出”补成正式契约
2. 让 `assetId` 成为跨层传输的唯一媒体主引用
3. 让 `SessionRuntime` 负责统一投影
4. 让 Host Adapter 负责最终渲染/发送

这样做的结果是：

- 现在可以先做 QQ 发图
- 后面可以自然扩到 GUI、音频、视频、Live2D
- 不会把文本链、blob 链、宿主协议链缠死在一起
