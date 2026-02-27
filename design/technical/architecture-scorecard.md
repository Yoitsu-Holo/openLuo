# 架构评分卡

## 总评

当前代码基线已经从”单体命令行原型”演进成”可扩展宿主 + 插件运行时 + 多角色 Agent 系统”。基础面明显稳固，SessionRuntime 统一门面和 `[GameApi]` 属性驱动路由已完成，多角色线程隔离仍有继续整理空间。

## 评分

| 维度 | 评分 | 说明 |
| --- | --- | --- |
| 模块边界 | 8/10 | `Modules/*` 的子域划分已经清晰 |
| 可扩展性 | 9/10 | 插件、skills、tools、subagents、背景、mods 均可扩展；`[GameApi]` 属性降低新增 API 成本 |
| Agent 基础设施 | 8/10 | runtime、tool-use loop、capability registry、inter-agent 已成形 |
| 数据层 | 8/10 | SQLite + schema migration + sqlite-vec 已满足当前规模 |
| 协议一致性 | 8/10 | 宿主命令、插件工具、Agent 能力三套入口已接近收敛；`[GameApi]` 属性统一了 plugin RPC dispatch |
| 可测试性 | 8/10 | 当前测试覆盖较广 |
| 可维护性 | 8/10 | `GameApiHandler` 从 ~230 行 switch 缩减为 ~70 行薄层；`GameApiDispatcher` 消除手写路由 |
| 观测性 | 6/10 | 有日志和 trace 片段，但系统级观测仍偏弱 |

## 优势

- 模块按稳定子域划分，基本没有再次堆回单体。
- Agent 人格与 kernel 分离，方向正确。
- 插件和宿主边界相对清晰，适合继续扩展。
- 测试已经覆盖 Agent、InterAgent、Infrastructure、Integration。
- `[GameApi]` 属性驱动路由：新增 API 只需加一个 attribute，dispatcher 自动扫描 + 参数绑定。
- `ISessionGameApi` / `SessionScopedGameApi`：前端通过 `session.Api.*` 调用，gameId 自动注入。

## 风险

- 角色间真实协作越来越复杂后，当前消息链可能需要更正式的线程模型。
- `GameBridge` 的 switch 路由已通过 `[GameApi]` + `GameApiDispatcher` 消除，但 handler 数量仍在增长，需持续关注分域。
- 旧背景文件职责仍偏重，下一步应通过角色原型 schema 与插件配置分层收口。

## 下一阶段最值钱的改动

1. 会话线程隔离
2. 能力/内容 schema 自动校验
3. 更完整的 trace / replay
4. 新 Memory API + executor-backed 特殊流程
