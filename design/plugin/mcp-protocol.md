# 当前 MCP 风格协议

## 1. 协议形态

当前插件协议是 JSON-RPC over stdio，由 `PluginProcess` 与 `McpPluginHost` 驱动。

启动后宿主会调用：

1. `initialize`
2. `tools/list`

之后再根据命令或 hook 调用：

- `tools/call`
- `hooks/call`

## 2. 宿主请求格式

宿主发送标准 JSON-RPC 请求：

```json
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"chat","arguments":{"message":"你好"}}}
```

## 3. 插件响应

插件返回带相同 `id` 的 `result`。

对于普通命令，宿主当前会从：

- `result.content[0].text`

中取文本，再反序列化为命令结果。

## 4. 反向宿主请求

插件可以反向发起带 `method` 的 JSON-RPC 请求，例如：

- `game/session/get`
- `game/state/query`
- `game/timeline/create`
- `game/asset/create`

宿主由 `GameApiHandler`（薄层代理）接收，转交 `GameApiDispatcher` 按 `[GameApi]` 属性路由到对应 handler 方法。dispatch 时自动完成参数绑定（`gameId` 从 bridgeContext 注入，其他参数按 camelCase 名称从 JSON params 绑定）。

注意：

以下历史接口已从协议面删除，插件调用会被视为未知接口：

- 旧 `game/narrative/*`
- 旧 `game/llm/complete`
- 旧 `game/agent/plan`
- 旧 `game/give`
- 旧 `game/memory/*`

插件不应继续依赖这些接口。需要叙事、记忆或 Agent 能力时，应先设计新的稳定 `game/*` 契约或 executor-backed 能力入口。

当前稳定接口面以 [gamebridge-api-surface.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/gamebridge-api-surface.md) 为准。

## 5. 通知

当前支持至少一种插件通知：

- `stream/output`

用于把插件输出流式写回 CLI 或 TUI。

## 6. 当前与标准 MCP 的关系

- 设计风格接近 MCP 的 tool/hook 模式
- 但实现是项目内自定义宿主协议
- 实际兼容面应以当前 `PluginProcess` / `McpPluginHost` 代码为准
