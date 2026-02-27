# openLuo Design

本目录同时记录两条迁移期事实：现有 `openLuo/Modules` 业务宿主链路，以及已落地的“通用 Agent 内核 + 领域扩展”新架构。新架构的权威契约与实施状态在仓库根目录 `todos/00-architecture.md` 至 `todos/11-implementation-steps.md`。

## 新架构导航

1. `../todos/00-architecture.md` — 总体决策与依赖拓扑
2. `../todos/01-project-layout.md` — 工程、扩展目录与配置布局
3. `../todos/02-kernel-contracts.md` — Capability/Context/Output 契约
4. `../todos/03-agent-context.md` — 上下文快照与会话端口
5. `../todos/04-capability-runtime.md` — 决策循环、并行、mutation、预算
6. `../todos/05-extension-system.md` — manifest、依赖、程序集加载与命名空间化
7. `../todos/06-mcp-integration.md` — MCP 适配器
8. `../todos/07-domain-extensions.md` — memory/companion/world/party/media
9. `../todos/08-platform-adapters.md` — CLI、宿主、SQLite、Playground
10. `../todos/10-testing.md` — 测试分层与 E2E 验收
11. `../todos/11-implementation-steps.md` — 实施清单

## 代码入口

- 内核：`openLuo.Capabilities/`、`openLuo.AgentContext/`
- 协议桥接：`openLuo.Capabilities.Llm/`、`openLuo.Capabilities.Mcp/`、`openLuo.Capabilities.A2A/`
- 扩展宿主：`openLuo.Extensions.Host/`
- 领域扩展：`extensions/*/`
- 平台：`openLuo.Cli/`、`openLuo.Tui/`、`openLuo.Gui/`、`openLuo.Qqbot/`
- 组合根：`openLuo/Composition/`、`openLuo/Infrastructure/Conversation/`
- 最小演示：`openLuo.playgraound/`（程序集名 `openLuo.Playground`）

## 迁移期旧文档

`technical/`、`gameplay/`、`plugin/`、`story/`、`implementation/`、`background/`、`mod/` 描述既有产品与业务链路。若旧文档与 `todos/` 的新架构决策冲突，以 `todos/` 和代码事实为准。
