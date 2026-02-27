# GameBridge Module

负责插件运行时与游戏业务之间的桥接。

包含：
- `GameApiHandler`
- 业务 handler（state / timeline / shop / gift / player / asset / host bridge）

边界：
- 不负责插件进程宿主和协议层
- 只负责把 `game/*` API 请求映射到游戏业务服务
- 不应承载第二套独立 AI 主链；旧 `narrative / ai` 接口已删除

当前接口治理原则：

- 保留稳定宿主能力接口：`session / time / character / inventory / shop / gift / commands / lifecycle / diary / state / asset / timeline`
- 不再保留旧 `narrative / llm / agent-plan / give / legacy memory` 直调接口

详细清单见：

- [gamebridge-api-surface.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/gamebridge-api-surface.md)
