# Commanding Module

负责命令解析、命令结果、命令门控上下文与命令门控接口。

包含：
- `Core/Models`：`ParsedCommand`、`CommandResult`、`InvocationKind`、`CommandGate*`
- `Core/Interfaces`：`ICommandGate`

边界：
- 对外提供命令协议与门控契约
- 不直接承载具体玩法执行逻辑
- 不负责宿主装配
