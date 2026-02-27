# 插件开发指南

## 1. 创建插件

在 `openLuo/data/plugins/` 下创建目录，例如：

```text
openLuo/data/plugins/my_plugin/
  plugin.jsonc
  main.py
```

## 2. 编写 manifest

最小示例：

```jsonc
{
  "id": "my_plugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "entry": "main.py",
  "hooks": []
}
```

## 3. 实现最小能力

插件启动后应能处理：

- `initialize`
- `tools/list`
- `tools/call`

如果声明了 hooks，还要处理：

- `hooks/call`

建议：

- 新插件的 hook 统一优先实现 `hooks/call`
- `tools/call` 只用于显式命令或工具
- 宿主当前会对部分 typed hook 提供 `hooks/call -> tools/call` 的兼容回退，但这只是迁移兼容层，不应作为长期依赖

## 4. 设计建议

- 把功能暴露成小而稳定的工具。
- 通过 `category` 正确声明命令类型。
- 需要宿主数据时，通过 `game/*` API 请求，而不是在插件内复制状态。插件 `game/*` 调用由宿主侧 `GameApiHandler`（薄层代理）→ `GameApiDispatcher`（`[GameApi]` 属性路由 + 自动参数绑定）处理。
- 对长耗时动作设置合理 `timeoutSeconds`。
- typed hook 会逐步获得统一上下文注入，例如 `gameId`、`sessionId`、`bridgeContext`、`timeSnapshot`、`resourceSnapshot`。

## 5. 调试建议

- 查看 `logs/protocol` 下的协议日志
- 先从简单 `tools/list` / `tools/call` 开始验证
- 如果需要和玩家交互，优先输出稳定 JSON 结果，再考虑 `stream/output`

## 6. 当前内置插件可作为样例

- `builtin_system_commands`
- `builtin_world_state_core`
- `builtin_inventory_shop`
- `builtin_subagent_core`
- `example_dream_weaver`
