# openLuo Design

本目录基于当前代码基线重新整理，不沿用历史设计结论。

## 设计原则

- 代码事实优先：以 `openLuo/`、`openLuo.Tests/` 和 `openLuo/data/` 为准。
- 分类保留：继续沿用 `technical`、`gameplay`、`plugin`、`story`、`implementation`、`background`、`mod`、`todo`。
- 文档用途明确：描述“现在是什么”和“下一步应该怎么演进”，不描述已经废弃的旧方案。

## 当前项目轮廓

- 宿主：`.NET 10` 单可执行程序，支持 CLI 与 TUI 双入口。
- 核心：命令驱动的 AI 角色引擎，主循环由 `GameEngine` 调度。
- 扩展：内容扩展以 pack/schema/registry 组织，运行时扩展主要通过 Python 插件以 JSON-RPC over stdio 接入。
- 子系统：`WorldState`、`Assets`、`Memory`、`Executor`、`Agent`、`AgentCapabilities`、`InterAgent`、`PluginRuntime`、`GameBridge`。
- 数据：背景、mods、skills、tools、subagents、builtin plugins 均随程序发布。

## 推荐阅读顺序

1. `technical/architecture.md`
2. `technical/architecture.dot`
3. `technical/architecture-agent.dot`
4. `technical/architecture-plugin.dot`
5. `technical/architecture-memory.dot`
6. `technical/content-schema.md`
7. `technical/content-registry-bootstrap.md`
8. `technical/input-flow-runtime.md`
9. `technical/resource-system.md`
10. `technical/planned-execution-schema-convergence.md`
11. `technical/character-agent-input-spec.md`
12. `technical/io-architecture.md`
13. `technical/database-schema.md`
14. `technical/rag-memory.md`
15. `plugin/plugin-spec.md`
16. `background/background-spec.md`
17. `mod/mod-spec.md`
18. `gameplay/core-mechanics.md`
19. `implementation/implementation-status.md`
20. `todo/unified-session-runtime-architecture.md`
21. `todo/unified-session-runtime-roadmap.md`

## 文档边界

- `technical/` 描述运行时结构、数据流、数据库、内存检索和资源组织。
- `technical/content-schema.md` 是当前内容装配与 schema 设计的主入口。
- `technical/content-schema.md` 与 `technical/content-registry-bootstrap.md` 共同约束宿主侧最小 canonical schema、registry 和 bootstrap 规则。
- 当角色卡、物品、资源、工具、技能、pack 的字段定义与其它文档冲突时，以 `technical/content-schema.md` 为准。
- `gameplay/` 描述玩家可见规则、角色系统、命令语义、时间与经济。
- `plugin/` 描述插件协议、插件开发方式和宿主桥接。
- `story/` 描述当前内置背景与角色资料。
- `implementation/` 描述现状、测试与路线图。
- `todo/` 只记录仍需继续推进的重构计划。
