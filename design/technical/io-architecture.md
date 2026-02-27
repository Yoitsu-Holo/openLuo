# IO 架构

## 1. 玩家输入

### CLI

- `Console.ReadLine()` 读取玩家输入
- 交给 `IGameSession.SubmitAsync(SessionInput)`

### TUI

- `Terminal.Gui` 驱动
- 输入框事件触发 `IGameSession.SubmitAsync(SessionInput)`
- 输出通过 `TuiStreams` 回流到历史面板

### QQBot

- QQ 消息事件触发 `IGameSession.SubmitAsync(SessionInput)`（Ambient input）

## 2. 前端数据查询

前端（CLI/TUI/QQBot）通过 `session.Api.*` 查询游戏数据：

- `session.Api.GetStateAsync()` — 游戏状态
- `session.Api.GetTimeAsync()` — 当前时间
- `session.Api.ListShopItemsAsync()` — 商店物品
- `session.Api.GetResourceStatusAsync()` — 资源状态
- 等 21 个方法

`SessionScopedGameApi` 自动从 `SessionHandle` 解析 `gameId` 并注入 handler，前端代码无需手动传 gameId。

## 3. 宿主内部调用

- 命令在内存中以 `ParsedCommand` 形式流转
- 角色消息通过 `IAgentDispatcher` + mailbox 分发
- 模块间主要通过接口与 DI 通信
- `GameApiDispatcher` 在启动时反射扫描全部 `[GameApi]` 路由，运行时按路由查表 + 自动参数绑定

## 4. 插件 IO

### 宿主到插件

- 启动 Python 子进程
- 通过标准输入发送 JSON-RPC 请求

### 插件到宿主

- 标准输出返回 JSON-RPC 响应
- 插件也可以反向发起 `game/*` 请求，由 `GameApiHandler`（薄层代理）→ `GameApiDispatcher` 路由到对应 `[GameApi]`-annotated handler 方法

### 插件流式输出

- 通过 `stream/output` notification
- 最终可落到 CLI 输出或 TUI 历史面板

## 5. 外部网络 IO

- LLM、Embedding、Rerank 都通过 `HttpClient` 调用外部供应商
- Provider 由 `Modules.Llm` 负责适配

## 6. 数据 IO

- 主持久化：SQLite
- 向量检索：SQLite + `sqlite-vec`
- 静态内容：`openLuo/data/*`
- 日志：`logs/` 与协议日志目录

## 7. 当前特点

- 交互 IO、插件 IO、模型 IO、数据库 IO 已被模块化分离。
- 插件与宿主之间是进程边界，而不是脚本内嵌。
- 玩家侧和 Agent 侧都能触发能力执行，但执行面最终会汇到宿主能力体系。
- 前端通过 `session.Api.*`（`SessionScopedGameApi`）查询数据，gameId 自动解析。
- 插件 `game/*` 请求通过 `[GameApi]` 属性 + `GameApiDispatcher` 自动路由。
