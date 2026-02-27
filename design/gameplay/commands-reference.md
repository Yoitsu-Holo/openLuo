# 命令参考

## 1. 前缀体系

当前命令前缀由 `GameEngine.ParseCommand` 定义：

- `/`：普通命令
- `$`：skill
- `&`：subagent
- `@`：tool

## 2. companion 内建命令

当前内建 companion 命令由 `CompanionOrchestrator` 负责：

- `/chat`
- `/task`
- `/party`
- `/characters`
- `/switch`
- `/task_status`
- `/taskstatus`

## 3. 插件命令

插件通过 `tools/list` 注册命令，宿主会把它们映射成：

- `/` 命令
- `$` skill
- `&` subagent
- `@` tool

映射依据是插件声明的 `category`。

## 4. 常见参数

- `--as <角色>`：指定执行角色
- `--confirm yes|true|1`：显式确认高风险能力
- `--trace true`：部分命令可展示 Agent trace

## 5. 路由规则

- companion 命令优先由 `CompanionOrchestrator` 处理
- 其余命令由 `AgentInvocationRouter` 转发到插件桥接
- skill/tool/subagent 执行结果会记录 party task

## 6. 当前命令设计特点

- 玩家、技能、工具、子代理四类调用共用一套命令解析器
- 命令结果统一为 `CommandResult`
- 高风险命令支持确认握手
