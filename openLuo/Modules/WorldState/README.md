# WorldState Module

负责状态系统、时间系统与时间线系统。

包含：
- `Application/Services`：`TimeService` 与 providers
- `Core/Interfaces`：`IState*`、`ITime*`、`ITimelineService`
- `Core/Models`：State / Time / Timeline 模型
- `Infrastructure/`：State 与 Timeline 实现

边界：
- 提供全局世界状态与推进机制
- 被玩法、叙事、插件桥接共同使用
- 对外只暴露 `openLuo.Modules.WorldState.*` 命名空间，不再通过全局 `Core`、旧 `Infrastructure.State/Timeline` 或旧 `Application.Services.Time*` 暴露模块专属契约
