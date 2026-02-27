# 路线图（2026-06 更新）

本路线图基于当前代码基线，不继承旧阶段命名。

## 已完成（自 2026-05 以来）

- `[GameApi]` 属性驱动路由 + `GameApiDispatcher`（替换 ~230 行 switch）
- `ISessionGameApi` / `SessionScopedGameApi`（前端 21 方法，自动注入 gameId）
- `IGameSession` / `IGameSessionCatalog` 会话生命周期门面
- CLI/TUI/QQBot 已收敛为 adapter 模式

## P1. 会话与协作收口

- 拆分玩家主对话线程、角色间 backchannel、任务线程
- 继续完善 inter-agent 协议和协作能力
- 明确 runtime 生命周期与上下文边界

## P2. 能力面治理

- 统一能力 schema
- 把插件能力、宿主能力和内容资源做自动校验
- 改善高风险能力确认链与错误回传

## P3. 观测与调试

- 补齐更完整的 trace
- 引入 replay / transcript 导出
- 增强协议和消息流可视化

## P4. 内容系统增强

- 背景与角色资料拆出更清晰 schema
- 强化 mod、plugin、background 的版本兼容规则
- 提升角色长期记忆与关系演进能力

## P5. 产品层扩展

- GUI
- 机器人接入
- 语音
- 多模态
- Live2D
