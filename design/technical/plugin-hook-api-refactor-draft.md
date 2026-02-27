# 插件系统与 Hook API 重构草案

本文档是当前 `PluginRuntime + GameBridge + hook` 体系的重构草案。

目标不是一次性推翻现有插件系统，而是：

- 保留当前可工作的插件进程模型
- 收紧 hook 契约
- 补足缺失的稳定宿主能力入口
- 去除历史 narrative 时代遗留的松散结果结构
- 为后续 Memory、多媒体、约会流程、命令后处理等能力提供可持续扩展面

## 1. 当前现状

当前插件系统已经具备以下能力：

- JSON-RPC over stdio 插件进程模型
- `initialize`
- `tools/list`
- `tools/call`
- 部分 typed hook 使用 `hooks/call`
- 反向宿主请求 `game/*`
- 动态命令注册
- agent flow 注册

当前 C# 侧关键入口：

- `IPluginHost`
- `PluginProcess`
- `McpPluginHost`
- `GameApiHandler`（薄层 JSON-RPC 代理，~70 行）
- `GameApiDispatcher`（启动时反射扫描 `[GameApi]` 属性，运行时按路由查表 + 自动参数绑定）

当前已存在的 hook 使用面：

- `onStartup`
- `onPromptContext`
- `onStatusQuery`
- `onSleepAfter`
- `onDayStart`
- `onScheduleDue`
- `onCommandAfter`

## 2. 当前问题

### 2.1 Hook 调用协议不统一

当前宿主存在两条 hook 调用路径：

- 通用 hook 走 `tools/call`
- typed hook `onPromptContext` / `onStatusQuery` 走 `hooks/call`

但内置插件并未统一实现 `hooks/call`。

结果是：

- 有些插件只实现 `tools/call`
- 有些插件同时实现 `tools/call` 和 `hooks/call`
- hook 是否能被 typed 路径消费，取决于插件作者是否记得补第二套分发逻辑

这会造成契约断层，不适合作为长期稳定接口。

### 2.2 Hook 输入模型过薄

当前 typed hook DTO 只有：

- `OnPromptContextInput`
- `OnStatusQueryInput`

主要字段集中在：

- `characterId`
- `interactionType`
- `playerMessage`
- `beatSummary`
- `moodSignals`
- `availableResources`
- `stateSnapshot`
- `pluginConfigs`

缺失明显上下文：

- `gameId`
- `sessionId`
- 时间快照
- 当前角色结构信息
- 输入来源信息
- 资源定义级视图
- 当前对话窗口
- 桥接上下文

插件只能拿到弱语义字典，无法稳定做更复杂的判定。

### 2.3 通用 Hook 输出结构是历史杂物箱

当前 `PluginHookResult` 同时承载：

- `AdditionalText`
- `SystemPrompt`
- `MemoryToStore`
- `AffectionDelta`
- `AffectionMultiplier`
- `StaminaBonus`
- `DreamText`
- `ModifiedPrompt`
- `Cancel`

这类设计来自旧 narrative 时代。

问题在于：

- 字段含义跨多个阶段
- 同一结构被不同 hook 复用
- 大量字段已经没有清晰消费路径
- 后续继续扩展只会继续堆字段

这不适合继续作为主扩展面。

### 2.4 插件缺少几条新的稳定宿主入口

旧 `game/narrative/*` 和 `game/memory/*` 已删除，这个方向是对的。

但删除后仍有真实业务缺口：

- 插件没有新的稳定 Memory API
- 插件没有合法触发“特殊互动流程”的 executor-backed 入口
- 插件拿不到工具执行完成事件
- 插件拿不到聊天完成后的结构化结果

因此一些旧能力只能被下线，而不是迁移。

## 3. 重构目标

重构后希望达到：

- hook 协议统一
- hook 输入输出强类型化
- 通用 hook 与 typed hook 职责分层清晰
- `game/*` 只暴露稳定宿主能力，不再泄漏第二套 AI 主链
- 插件能观察宿主关键事件，但不能私自重建宿主主链
- 旧插件可渐进迁移，不要求一次性全量重写

## 4. 建议的新分层

建议将插件扩展面明确分成三层：

### 4.1 Plugin RPC Layer

这是进程通信层。

保留：

- `initialize`
- `tools/list`
- `tools/call`
- `hooks/call`
- 插件反向 `game/*`
- 通知，如 `stream/output`

这一层只负责协议，不承载业务语义。

### 4.2 Hook Contract Layer

这是宿主事件扩展层。

所有 hook 应统一走：

- `hooks/call`

`tools/call` 只用于：

- 用户命令
- 插件显式工具

不再建议把 hook 也伪装成 command/tool。

### 4.3 Host Capability Layer

这是插件反向调用宿主的稳定业务能力层，也就是：

- `game/session/*`
- `game/time/*`
- `game/character/*`
- `game/state/*`
- `game/resource/*`
- `game/asset/*`
- `game/timeline/*`
- `game/commands/*`

新增能力也应落在这一层，而不是重新开放 narrative 黑盒。

## 5. 建议保留的 Hook

以下 hook 继续保留，但应改为统一 typed 契约：

- `onStartup`
- `onPromptContext`
- `onStatusQuery`
- `onSleepAfter`
- `onDayStart`
- `onScheduleDue`
- `onCommandAfter`

其中：

- `onStartup`：插件注册资源、flow、初始检查
- `onPromptContext`：向聊天 / 结算 / 特殊流程注入附加上下文
- `onStatusQuery`：补充状态面板视图
- `onSleepAfter` / `onDayStart`：生命周期事件
- `onScheduleDue`：timeline 到期事件
- `onCommandAfter`：命令后观察点

## 6. 建议新增的 Hook

### 6.1 `onChatAfter`

用途：

- 聊天完成后观察最终可见输出
- 记录插件侧派生事件
- 做非阻塞后处理

建议输入：

- `gameId`
- `sessionId`
- `characterId`
- `archetypeId`
- `playerMessage`
- `finalReply`
- `visibleBlocks`
- `outputBlocks`
- `traceLines`
- `presentationProfile`
- `timeSnapshot`
- `resourceSnapshot`
- `bridgeContext`

建议输出：

- `additionalText`
- `notices`
- `memoryWrites`（可选，若后续采用结构化 memory hook）

注意：

- 不能直接改写本轮主回复
- 只能做追加观察或衍生动作

### 6.2 `onToolExecuted`

用途：

- 观察 executor / capability 工具执行结果
- 接入图片、音频、视频、资产解锁等能力

建议输入：

- `gameId`
- `sessionId`
- `characterId`
- `toolName`
- `executorKind`
- `success`
- `reason`
- `args`
- `options`
- `textOutput`
- `structuredOutput`
- `assetIds`
- `error`
- `timeSnapshot`
- `bridgeContext`

建议输出：

- `additionalText`
- `followupActions`
- `assetDecorations`

## 7. 建议新增的宿主稳定 API

### 7.1 新 Memory API

建议新增：

- `game/memory/query`
- `game/memory/write`
- `game/memory/highlights_query`

这些接口必须基于新的 Memory DTO，而不是恢复旧 narrative memory 形态。

建议能力边界：

- 查询结构化记忆
- 写入结构化记忆
- 查询“重要记忆”投影

不直接暴露底层向量召回策略细节。

### 7.2 Executor-backed 特殊流程 API

这是为了替代旧 `/date` 一类特殊互动。

建议新增一类高层接口，例如：

- `game/executor/run_interaction`
- `game/executor/run_flow`

输入不再是“请帮我跑 narrative beat”，而是：

- 流程类型
- 角色
- 用户意图
- 受控上下文

这样插件申请的是“能力”，不是自己重开主链。

### 7.3 可选的输出事件 API

如果以后多媒体输出继续扩展，建议增加统一观察面，例如：

- `game/output/emit`

或者保持宿主内部输出主链不变，只通过 `onToolExecuted` 暴露结果。

当前更推荐后者，先不要扩张 `game/*`。

## 8. 建议统一的 Hook 输入基类

建议所有 typed hook 输入都继承统一基类，例如：

- `HookContextBase`

统一字段建议包括：

- `gameId`
- `sessionId`
- `characterId`
- `archetypeId`
- `clientType`
- `clientId`
- `sourceId`
- `channelId`
- `actorId`
- `reason`
- `pluginConfigs`
- `timeSnapshot`
- `bridgeContext`

其中 `bridgeContext` 不应只在反向 `game/*` 调用里可见，也应进入 typed hook DTO。

## 9. 建议统一的资源输入

当前 plugin hook 里只给：

- `availableResources`
- `stateSnapshot`

建议增加：

- `resourceDefinitions`
- `resourceValues`
- `resourceLifecycle`

或者至少提供：

- `resourceSnapshot`

让插件拿到的是资源系统的高层视图，而不是只能消费 `stateSnapshot` 的字典投影。

## 10. 兼容策略

建议分三阶段迁移。

### 阶段 A：协议统一但保留兼容

- 宿主新增统一 hook dispatcher
- typed hook 优先走 `hooks/call`
- 若插件未实现 `hooks/call`，可临时回退到 `tools/call`
- 给出日志警告，标记旧插件未迁移

### 阶段 B：补齐 typed DTO 与新能力

- 新增 `onChatAfter`
- 新增 `onToolExecuted`
- 新增 Memory API 草案实现
- 新增 executor-backed 特殊流程入口

### 阶段 C：收紧旧结构

- 废弃 `PluginHookResult` 的历史字段
- 将通用 hook 结果改为按 hook DTO 输出
- 移除 hook 的 `tools/call` 兼容路径

## 11. 不建议做的事

以下方向不建议继续：

- 恢复 `game/narrative/*`
- 恢复 `game/llm/complete`
- 恢复 `game/agent/plan`
- 把更多内核私有逻辑塞进通用 `PluginHookResult`
- 让插件直接拼接宿主主回复文本并绕过 Agent/Executor

这些都会把 `GameBridge` 再次推回“第二套 AI 主链”的老路。

## 12. 建议优先级

### P0

- 统一 hook 调用协议
- 建立 typed hook 基类上下文

### P1

- 新增 `onChatAfter`
- 新增 `onToolExecuted`
- 设计新 Memory API

### P2

- 设计 executor-backed 特殊流程 API
- 拆解 `PluginHookResult`

## 13. 预期收益

完成上述重构后：

- 插件系统协议更稳定
- hook 可维护性更高
- 宿主主链边界更清晰
- 多媒体、memory、约会、资产等功能能走正当扩展面
- 不会再回到旧 narrative 插件时代那种“每个插件私自开一套 AI 链”的状态

## 14. 后续文档

如果这份草案确认，可以继续拆成两份正式设计：

1. `plugin-hook-contract-v2.md`
2. `gamebridge-capability-expansion.md`

前者解决 hook 协议和 DTO。

后者解决 Memory API、executor-backed 特殊流程、多媒体相关宿主能力入口。

## 15. 第一阶段最小落地

第一阶段只做最小可落地闭环，不在本阶段处理新的 Memory API 或 executor-backed 特殊流程。

范围：

- typed hook 补统一基类上下文
- typed hook 统一走 `hooks/call`
- 宿主提供 `hooks/call -> tools/call` 兼容回退
- 将历史 `onChatBefore` 重新接入宿主聊天前置链
- 新增 `onChatAfter` 观察型 hook

本阶段明确不做：

- 恢复旧 narrative API
- 恢复旧 memory API
- 让插件改写主聊天回复
- 让 `onChatAfter` 直接驱动高风险副作用
