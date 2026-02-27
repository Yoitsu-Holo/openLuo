# Assets Module

负责资产定义、记录、blob、meta、link 与 unlock。

包含：
- `Core/Interfaces`：`IAsset*`
- `Core/Models`：`Asset*`、`EntityLink`、`UnlockRecord`
- `Infrastructure/`：资产存储与查询实现

边界：
- 作为独立资产子域
- 通过 API handler 与玩法层交互
- 对外只暴露 `openLuo.Modules.Assets.*` 命名空间，不再通过全局 `Core` 或旧 `Infrastructure.Assets` 暴露模块专属契约
