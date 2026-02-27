# 01 · 工程拆分与目录结构

## 1. 解决方案布局

```text
openLuo.slnx
├── openLuo.Foundation/
├── openLuo.Llm/
├── openLuo.Embedding/
├── openLuo.Memory/
├── openLuo.AgentContext/
├── openLuo.Capabilities/
├── openLuo.Capabilities.Llm/
├── openLuo.Capabilities.Mcp/
├── openLuo.Cli/
├── openLuo.Tui/
├── openLuo.Gui/
├── openLuo.Qqbot/
├── openLuo/                    （入口 + 组合根）
├── openLuo.Playground/
├── extensions/                 （不编译进 slnx；由扩展宿主运行时加载）
└── tests/
    ├── openLuo.Capabilities.Tests/
    ├── openLuo.AgentContext.Tests/
    ├── openLuo.Extensions.Tests/
    ├── openLuo.Cli.Tests/
    └── openLuo.E2E.Tests/
```

## 2. 各工程职责与依赖

| 工程 | 职责 | 依赖 | 说明 |
|---|---|---|---|
| `openLuo.Foundation` | Block/IGameLogger/Logger/PromptSanitizer/DB 连接工厂 | 无 | 已存在，保留 |
| `openLuo.AgentContext` | 上下文快照/Contributor/Assembler/Session/IConversationStore 端口 | Foundation + Capabilities | 新工程 |
| `openLuo.Embedding` | IEmbeddingClient + EmbeddingConfig | Foundation | 已存在，保留 |
| `openLuo.Memory` | 记忆仓储/检索/写入 | Foundation + Embedding | 已存在，保留 |
| `openLuo.AgentContext` | 上下文快照/Contributor/Assembler/Session/IConversationStore 端口 | Foundation | 新工程 |
| `openLuo.Capabilities` | 能力目录/调度/决策循环/策略/预算/mutation merge/Workflow 运行时/输出管道 | Foundation | 新工程，不依赖 Llm |
| `openLuo.Capabilities.Llm` | ICapabilityDecisionModel 的 LLM 实现 | Capabilities + Llm | 新工程 |
| `openLuo.Capabilities.Mcp` | MCP 连接/发现/调用（官方 SDK） | Capabilities + ModelContextProtocol | 新工程 |
| `openLuo.Cli` | CLI 平台适配（渲染/输入/输出订阅） | AgentContext + Capabilities | 新工程 |
| `openLuo.Tui` | TUI 平台适配 | 同上 | 新工程，第一版不硬验收 |
| `openLuo.Gui` | GUI 平台适配 | 同上 | 新工程，第一版不硬验收 |
| `openLuo.Qqbot` | QQ bot 平台适配 | 同上 | 新工程，第一版不硬验收 |
| `openLuo` | 入口 + 组合根 + 配置加载 + Extension Host 装配 + SQLite 对话存储 | 全部 | 瘦身 |
| `openLuo.Playground` | 新架构最小 demo（能力注册/决策循环/扩展编写示例） | 按需 | 重写，承担部分 E2E |
| `extensions/*` | 领域扩展（独立程序集） | Capabilities/AgentContext/Llm/Memory + 宿主端口 | 运行时加载 |

## 3. 命名空间策略

- 所有工程 RootNamespace = `openLuo`（与现有子工程一致），AssemblyName 各自独立。
- 扩展程序集建议 RootNamespace = `OpenLuo.Extensions.<Id>`（避免与内核混淆），AssemblyName = `openLuo.Extension.<Id>`。
- 平台适配层命名空间：`openLuo.Interfaces.Cli` 等（沿用现有 Interfaces 目录语义，但拆工程）。

## 4. 扩展目录布局

```text
extensions/
  memory/
    extension.jsonc
    openLuo.Extension.Memory.dll
    data/                       （记忆规则等，可选）
  companion/
    extension.jsonc
    openLuo.Extension.Companion.dll
    data/                       （角色原型、skills 等）
  world/
    extension.jsonc
    openLuo.Extension.World.dll
    data/                       （物品、分类、时段窗口、状态定义）
  party/
    extension.jsonc
    openLuo.Extension.Party.dll
  media/
    extension.jsonc
    openLuo.Extension.Media.dll
```

目录名以 `.disable` 结尾（如 `world.disable/`）→ 宿主完全跳过（D24）。

## 5. 宿主目录结构（openLuo）

```text
openLuo/
  Program.cs                    入口（--cli / --tui / --gui / --qqbot 参数）
  Composition/
    ServiceCollectionExtensions.cs   组合根
    HostOptions.cs                   启动配置
  Host/
    ExtensionHost.cs                 扩展加载/依赖解析/装配
    ConfigurationLoader.cs           配置加载（config/*.jsonc）
  Infrastructure/
    SqliteConversationStore.cs       IConversationStore 的 SQLite 实现
  LlmAdapter/
    LlmCapabilityDecisionModel.cs    ICapabilityDecisionModel 的 LLM 实现
```

## 6. 配置布局

```text
config/
  app.jsonc               通用宿主配置（预算默认值、扩展目录路径）
  llm.jsonc               LLM 配置（已存在）
  mcp-servers.jsonc       MCP server 连接配置（D48）
```

领域数据不在 `config/`，随扩展走（D30/D43 配套：数据随扩展）。

## 7. 测试项目对应（D38）

| 测试项目 | 覆盖 |
|---|---|
| openLuo.Capabilities.Tests | 决策循环/快照 merge/幂等/并行约束/预算/Workflow 运行时/输出管道 |
| openLuo.AgentContext.Tests | Assembler/Contributor 独立性/预算裁剪/失败降级/标签渲染 |
| openLuo.Extensions.Tests | 5 个扩展各自能力/状态/标签/依赖解析 |
| openLuo.Cli.Tests | CLI 渲染/输入解析/输出订阅 |
| openLuo.E2E.Tests | CLI 端到端全流程（除多媒体） |
