# PluginRuntime Module

负责插件进程宿主、JSON-RPC 通信、工具注册与 `game/*` 反向 API 桥接。

包含：
- `Core/Interfaces`：插件宿主和 API mediator
- `Core/Models`：hook 上下文模型
- `Infrastructure/`：宿主、进程、bridge、handlers

边界：
- 提供通用插件运行时
- 通过 handlers 暴露具体业务 API
- 插件可以在 `plugin.jsonc` 中静态声明 `flows`，宿主在加载时会注册到 `IAgentFlowRegistry`

## Flow Registration

插件现在可以在 manifest 中声明最小 flow 注册信息：

```json
{
  "id": "example_flow_plugin",
  "name": "Example Flow Plugin",
  "version": "1.0.0",
  "entry": "main.py",
  "flows": [
    {
      "id": "example.character.demo",
      "startNodeId": "plan",
      "nodes": [
        { "id": "plan", "callName": "character.plan" },
        { "id": "done", "callName": "terminal.done" }
      ],
      "edges": [
        { "fromNodeId": "plan", "toNodeId": "done", "when": "规划完成后结束" }
      ]
    }
  ]
}
```

约束：

- 节点只声明 `id + callName`
- 边只声明 `fromNodeId + toNodeId + when`
- `node kind`、`output key`、内部 `edge id` 由 Agent 内部推导
- 实际副作用仍必须通过 `AgentCapabilities` 和可执行 `callName` 完成
