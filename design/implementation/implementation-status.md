# 实现状态（2026-06 更新）

## 已完成

- CLI / TUI / QQBot 三入口
- 数据库初始化与迁移
- Python 插件运行时
- `game/*` API 桥接：`[GameApi]` 属性驱动路由 + `GameApiDispatcher` 自动 dispatch（44 routes, 10 handlers）
- `ISessionGameApi` / `SessionScopedGameApi`：前端 21 个方法，gameId 自动注入
- `IGameSession` / `IGameSessionCatalog`：会话生命周期统一门面
- WorldState、Assets、Memory 基线
- Agent runtime、tool-use loop、capability registry
- inter-agent `ask_character` 和内部 session chat
- builtin backgrounds、mods、skills、tools、subagents、plugins
- 自动化测试基线通过

## 当前基线

- 构建：通过
- 测试：通过（GameBridge 专项 34 通过）

## 进行中主题

- 多角色线程隔离继续细化
- Agent runtime 与 inter-agent 能力继续增强
- 文档与代码进一步对齐

## 尚未完成

- 玩家主对话线程、内部会话线程、任务线程正式隔离
- 更强的观测与 replay
- 更系统的内容 schema 校验
- GUI、HTTP、语音、Live2D 等更高层产品能力
- 新 Memory API + executor-backed 特殊流程
