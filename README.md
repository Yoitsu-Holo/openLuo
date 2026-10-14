# openLuo（开源洛天依）

> 项目名称统一为 **openLuo**。

---

## 1. 项目简介

**openLuo** 是一个开源的"可扩展 AI 角色引擎"，目标是构建可持续演进的洛天依式虚拟角色交互体验底座。项目以"通用 Agent 内核 + 领域扩展"为架构核心：内核不绑定任何业务（RPG、桌宠还是聊天陪伴由加载的扩展决定），将 LLM 推理、能力调度、状态管理、记忆检索、多角色协作等能力解耦为独立模块，支持 CLI / TUI / QQ / GUI 四入口运行。

QQ bot 是当前生产主入口（`./openluo-qq`），通过 **OneBot 11**（LLBot 正向 WebSocket）对接 QQ 消息。

架构为**中心 Hub + 瘦客户端**：`./openLuo --serve` 启动 Hub（唯一持有内核：能力/记忆/状态/配置），
CLI / TUI / GUI / QQ 桥都是**各自独立的程序**（`openluo-cli` / `openluo-tui` / `openluo-gui` / `openluo-qq`），
经 HTTP 控制面 + WebSocket 数据面（`openLuo.Protocol`）连接 Hub；
客户端程序集**只依赖 `openLuo.Protocol` + `openLuo.Client`，不引用内核**（`OPENLUO_HUB_URL` 可指向远端 Hub），
服务端也不含任何 UI 驱动——两侧互不暴露，由 `ArchitectureBoundaryTests` 守着。

**文档索引**：

| 文档 | 内容 |
| --- | --- |
| [`README.md`](README.md) | 总览：技术栈、分层、配置、日志、构建发布、测试、路线图 |
| [`docs/architecture/layers.md`](docs/architecture/layers.md) | **架构检视**：分层图、实测依赖边、7 条边界规则（B1–B7）、技术债清单 |
| [`docs/architecture/hub-protocol.md`](docs/architecture/hub-protocol.md) | 中心 Hub 架构 + **Wire 协议 v1**（唯一跨边界契约，含实现状态标注） |
| [`docs/architecture/log-store.md`](docs/architecture/log-store.md) | 日志存储：热库（SQLite + FTS5）+ 冷文件 + 保留策略 |
| [`docs/architecture/context-budget.md`](docs/architecture/context-budget.md) | **上下文预算**：token 计数口径、保底/可裁区域、裁剪算法、被裁明细、配置真值表 |
| [`docs/architecture/context-domains.md`](docs/architecture/context-domains.md) | **上下文域键**：Global/Session/Thread/User 四个域的语义、键形状、内容归属与身份（userId）落地 |
| [`docs/architecture/context-spaces.md`](docs/architecture/context-spaces.md) | **上下文空间（设计稿，未实施）**：agent/user/group 三类空间、制品归属与方向性可见性、发布（写时派生）与 detail/summary、权限分层与主体确认、推理任务下发与迁移路径 |
| [`docs/architecture/workers-and-hooks.md`](docs/architecture/workers-and-hooks.md) | **Worker 与 Hook（设计稿，未实施）**：执行面边界——worker 声明/Run/统一触发源（事件·定时·随机）/上下文读写契约、hook 挂点与三种语义（观察·拦截·反应）、多 worker 并发约束 |
| [`docs/architecture/runtime-modules.md`](docs/architecture/runtime-modules.md) | **运行时模块划分（设计稿，未实施）**：Transport / Context（**容器 + 判定门 + Builder**）/ **UAC** / Worker 四模块与依赖 DAG、UAC 五个子职责、一个策略源三个执行缝、容器模型（含设备/健康等非文本容器）与**提示词缓存稳定性规则**、今天代码→未来归属 |
| [`docs/architecture/skills-workflows.md`](docs/architecture/skills-workflows.md) | **技能与工作流**：目录注入 + 按需使用（`core:load_skill` / `core:run_workflow`）、文件化技能格式、扩展作者指南 |
| [`docs/architecture/context-scope-risk.md`](docs/architecture/context-scope-risk.md) | 上下文「全局 + 局部」改造的风险识别与建议 |
| [`docs/todos.md`](docs/todos.md) | **待办项与缺陷项（登记表）**：已核实的缺陷、已存在但未接线/未实现的待办、待裁决问题——各带证据与关闭判据 |

## 2. 技术栈

| 层级          | 技术选型                                                                        | 归属工程                                        |
| ------------- | ------------------------------------------------------------------------------- | ----------------------------------------------- |
| 语言 / 运行时 | C# 13 / .NET 10                                                                 | 全仓                                            |
| 服务端宿主    | ASP.NET Core Kestrel（Minimal API + WebSocket）                                 | `openLuo.Server`                                |
| AI / LLM 抽象 | `Microsoft.Extensions.AI` 10.8.1（OpenAI provider，路由多模型）                 | `openLuo.Embedding`                             |
| 向量检索      | SQLite + Dapper + `sqlite-vec`（`SQLitePCLRaw.lib.e_sqlite3`，编译含 FTS5）     | `openLuo.Foundation` / `.Embedding` / `.Memory` |
| 持久化        | `Microsoft.Data.Sqlite` 10.0.10 + Dapper（`hub.db` / `game.db` / `logs/hot.db`）| `openLuo.Application` / `openLuo.Memory`        |
| 终端 UI       | Terminal.Gui 1.19.0                                                             | `openLuo.Tui`（`openLuo.Cli` 自绘 ANSI，无 UI 包） |
| 桌面 GUI      | Avalonia 11.3.18（Desktop / Fluent / Inter / Diagnostics）+ CommunityToolkit.Mvvm | `openLuo.Gui`                             |
| 远程能力      | MCP 官方 SDK（`ModelContextProtocol` 2.1.0）、A2A（1.0.0-preview2）             | `openLuo.Capabilities.Mcp` / `.A2A`             |
| 测试          | xUnit / NSubstitute；**10 个测试工程**分域（见 §8）                              | `tests/`                                        |

> **依赖瘦身原则**：宿主 exe `openLuo` **零 PackageReference**；UI 驱动、向量库、远程 SDK 各自归属使用它们的工程——
> 客户端只带自己需要的 UI 包，服务端只带内核与 Hub 需要的包（`openLuo.Application` 仅 5 个包：Sqlite / Dapper / DI / Http / Hosting）。

## 3. 架构：通用 Agent 内核 + 领域扩展

按层列出的程序集（规模数字与实测依赖边见 [`docs/architecture/layers.md`](docs/architecture/layers.md)）：

| 层         | 工程                                                              | 职责                                                                   | 依赖                                       |
| ---------- | ----------------------------------------------------------------- | ---------------------------------------------------------------------- | ------------------------------------------ |
| 协议 / 基础 | `openLuo.Protocol`                                                | Wire 契约：Envelope、消息类型表、DTO、错误码（唯一跨边界契约）          | **零本仓依赖**                             |
| 协议 / 基础 | `openLuo.Foundation`                                              | 基础设施端口与实现（日志、配置中心、SQLite、资产端口…）                  | 零本仓依赖                                 |
| 协议 / 基础 | `openLuo.OneBot`                                                  | OneBot 11 客户端（QQ 桥专用）                                           | 零本仓依赖                                 |
| 基础能力   | `openLuo.Domain` / `.Llm` / `.Embedding` / `.Modules.WorldState`  | 领域模型、LLM 路由、向量化、世界状态库                                  | `Foundation`                               |
| 内核       | `openLuo.Capabilities`                                            | 能力目录、决策循环、并行调度、状态事务、输出队列、确认网关              | `Foundation`                               |
| 内核       | `openLuo.AgentContext`                                            | 上下文装配与回合快照                                                    | `Foundation` + `Capabilities`              |
| 内核       | `openLuo.Memory`                                                  | RAG 记忆（sqlite-vec + 降级回退）                                       | `Foundation` + `Embedding`                 |
| 内核       | `openLuo.Modules.Agent`                                           | 角色 Agent 运行层（多角色协作）                                         | `Capabilities` + `Domain`                  |
| 内核桥接   | `openLuo.Capabilities.Llm` / `.Mcp` / `.A2A`                      | LLM 原生 tool calls、MCP、Agent2Agent 远程能力                          | `Capabilities`（+ `Llm`）                  |
| 扩展契约   | `openLuo.Abstractions`                                            | 扩展契约（`IAgentExtension` 等）+ manifest 加载                        | `AgentContext` + `Capabilities`            |
| 领域扩展   | `extensions/{memory,companion,world,party,sticker,music}`          | 记忆、伴侣人格、世界状态、多角色、表情、音乐分享卡                      | `Abstractions` + 各自模块（**不引宿主 exe**） |
| 网络       | `openLuo.Server`                                                  | Hub：HTTP 控制面 + WS 数据面，把 `IAgentRuntime` 映射为 wire 消息        | `Protocol` + `Capabilities`                |
| 装配       | `openLuo.Application`                                             | 组合根 + 配置/作业/调度/日志/资产/会话存储 + 启动编排（`KernelStartup` / `HubStartup`） | 内核 + `Server` + `Abstractions`  |
| 入口       | `openLuo`（可执行）                                                | **纯入口**（`Program.cs` 50 行）：`--serve` → 启动内核 → 运行 Hub → 关闭 | 仅 `openLuo.Application`（**零包依赖**）   |
| 客户端 SDK | `openLuo.Client`                                                  | 连接、握手协商、回合流式、会话句柄                                      | `Protocol`                                 |
| 客户端     | `openLuo.Cli` / `.Tui` / `.Gui` / `.Qqbot`（各为**可执行**）       | 产物 `openluo-cli` / `openluo-tui` / `openluo-gui` / `openluo-qq`       | `Client` + `Protocol`（QQ 桥另加 `OneBot`） |
| Demo       | `openLuo.playground`（程序集名 `openLuo.Playground`）             | 内核能力循环最小演示，离线确定性；`make run-playground`                | `Application` + 内核（**不引宿主 exe**）    |

架构约定：

- 内核零业务配置硬编码；领域数据随扩展走
- 扩展注册的 `canonicalId` 自动命名空间化为 `<extension-id>:<local-id>`；`core:` 保留给内核
- 领域扩展目录以 `.disable` 结尾时完全跳过
- **边界规则（7 条：B1–B6 由 `ArchitectureBoundaryTests` 8 例守护，B7 由 `HttpSurfaceTests` 5 例守护）**：① 协议零依赖；② 客户端不含内核（只 `Protocol` + `Client`）；③ 服务端不自带内核实现（只 `Protocol` + `Capabilities`）；④ 宿主 exe 只依赖 `Application`；⑤ 扩展不引宿主 exe；⑥ `Application` 不反向依赖 exe / 客户端 / UI 驱动；⑦ **HTTP 面常量全部集中在 Protocol**（`HttpPaths` / `HttpQueryKeys` / `HttpResponseTypes` / `HttpRules`，禁止字面量；状态码映射单点 `ErrorCodes.HttpStatusOf`）且遵循 **REST**（路径只放资源名词、创建回 `201 + Location`、查询串不承载资源身份）
- 内核侧的目录归属已收敛到 `openLuo.Application/{Hosting,Composition,Modules/AppShell,Infrastructure,Startup}`；宿主 `openLuo/` 只剩 `Program.cs` + `data/` + `native/`
- **上下文域（D45）**：内容按 `ContextScope`（Global / Session / Thread / User）归属，键形状由 `ContextDomainKeys` 唯一定义——身份/persona/时间/规则/能力清单是 Global（所有会话共用），世界状态/历史/在场是 Session，长期记忆是 **User（跨会话的用户↔角色）**。说话人用**稳定身份**（平台 userId，如 QQ 号）归因：`speaker_id` 存稳定 id、`speaker_name` 存显示名（渲染给模型用显示名）；平台没给身份时回退到会话主体（改造前行为）——详见 [`docs/architecture/context-domains.md`](docs/architecture/context-domains.md)

## 4. 能力与协议

- **MCP**：`openLuo.Capabilities.Mcp` 使用官方 `ModelContextProtocol`，支持 stdio / http / streamable-http 三种传输，per-server 请求头（含 `{env:VAR}` 占位展开）；连接失败结构化降级（server 标记不可用，不阻塞宿主）。server 列表见 `config/mcp-servers.jsonc`。
- **MCP 工具失败语义（重要约定）**：工具失败必须让结果带 `isError=true` —— 客户端据此记 `CapabilityStatus.Failed`（调度日志 / 轨迹如实为 `failed`），并把错误原文同时放进 `Text` 与 `Error` 回填给模型。
  Python MCP 服务器（`mcp/`）因此**不能把异常吞成普通文本返回**（那会被记成 `ok`，真实错误只剩日志可查）：应 `raise ToolError(f"...失败: {ex}") from ex`（`mcp.server.mcpserver.exceptions`），或让异常自然冒泡——官方 SDK 会自动转成 `isError`。
- **A2A**：`openLuo.Capabilities.A2A` 通过 Agent Card 发现 skills 并映射为 `RemoteAgent` 能力。
- **原生 tool calls**：LLM 桥接只把无 tool-call 非空文本视为最终回复；tool-call 结果进入决策循环继续规划。
- **工具调度**：`DefaultCapabilityDispatcher` 并行调度 + 决策循环（预算/终止条件/非法并行拒绝），调度日志归 `agent/dispatch`（start / ok / failed / batch done）。
- **Inter-Agent**：最小 `ask_character` + `AgentAsk / AgentReply` 消息协议，角色间真实通信。
- **记忆 / 状态 / 调度 / 资产**：RAG 记忆检索（sqlite-vec + 降级回退）、状态事务（mutation 冲突检测）、定时/一次性调度（协议 `/v1/schedules` 到期发起**主动回合**）、资产与解锁。
- **多端上下文**：会话按 `(subjectId, agentId, conversationId)` **复用** → PC 客户端与手机 QQ 私聊共享同一段对话；世界状态只读投影（`GET /v1/sessions/{id}/state` + `state.updated`，按 subject 分区、只推变化键、**快照持久化**）；`output.ack` / `output.fail` 投递回执（QQ 桥在 OneBot 真正发出后才 ack）；`turn.cancel` 取消进行中回合；`presence.subscribe{sessionIds}` 按会话过滤在场。详见 hub-protocol §5.4 / §6.1 / §6.2 / §6.4。
- **Hub 持久化**：`hub.db`（SQLite WAL，独立于业务 `game.db`）承载会话 / 作业 / 调度 / 令牌 / 轨迹 / 世界状态快照；重启**保守恢复**（会话恢复、进行中作业标记 failed、令牌仍有效）。
- **入口治理**：Hub 在**鉴权之前**限流 + 输入校验（`security.jsonc` 的 `hub*` 键，可热加载）：HTTP/WS 每 key 速率（`429 + 8001`）、路径与 WS 会话 id 白名单（`4003`）、单帧上限（`2002`）、回合文本上限与严格模式（`5004`）——详见 hub-protocol §4.9。
- **技能与工作流**：每轮上下文注入**摘要目录**（`[Skills]` / `[Workflows]` / `[RemoteAgents]`）；技能用 `core:load_skill` 按 id **登记引用**，正文随后以 `[LoadedSkills]` 系统块注入（**引用展开**：上下文里永远是最新版本、正文不算对话内容、走可裁的系统块预算），用完可用 `core:unload_skill` 卸载；工作流用 `core:run_workflow` 黑盒执行多步编排。技能有两个来源：扩展 `AddSkill(...)`（代码侧）与 **`data/skills/*.md`**（文件侧，**加/改技能不用重编译**，**重启即更新**——有意不做热加载）；工作流与它的节点处理器必须成对注册。详见 [`docs/architecture/skills-workflows.md`](docs/architecture/skills-workflows.md)。（目录与登记已接线；**调用未绑定 invoker**，端到端尚不可用，见 [`docs/todos.md`](docs/todos.md) D1）
- **协议域**：会话、回合流式、输出与资产、能力目录、配置（`GET` / `PATCH` / `DELETE`）、作业（`/v1/jobs`）、调度（`/v1/schedules`）、观测（`/v1/metrics` / `/v1/traces`）、日志（`/v1/logs`、`/v1/logs/archive`）、状态投影、鉴权（`POST /v1/tokens`）——详见 `docs/architecture/hub-protocol.md`。
  **HTTP 面遵循 REST**：路径只用资源名词（动作交给方法）、创建回 `201 + Location`、查询串只承载过滤/分页/视图；
  路径、查询键、响应 `type`、资源 id 白名单全部是 Protocol 常量（`HttpPaths` / `HttpQueryKeys` / `HttpResponseTypes` / `HttpRules`），
  错误码 → HTTP 状态码单点在 `ErrorCodes.HttpStatusOf`——`HttpSurfaceTests` 守形状，禁止散落字面量。
  文档即单一来源：规范内未实现者就地标 **v1 未实现**（可 grep），已声明但未处理的命令回 `2004`、规范外类型回 `2003`（`HubProtocolSurfaceTests` 强制"每个消息类型 ∈ 已实现 ∪ 预留清单"）。

## 5. 配置

运行时从 `./config/` 加载 `{name}.jsonc`（模板在 `openLuo/data/config/*.example.jsonc`，共 10 份）：

| 配置                                    | 说明                                                                                                    |
| --------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `llm.jsonc`                             | LLM 路由（多 route：模型/供应商/API key/temperature/maxTokens/超时/重试/限速/能力开关 + 路由策略）        |
| `mcp-servers.jsonc`                     | MCP server 列表（传输、URL/command、per-server 请求头、`env`）                                           |
| `a2a-agents.jsonc`                      | A2A 远端 Agent 列表（`agents[]`，按路径直读的整份配置）                                                  |
| `qqbot.jsonc`                           | QQ bot 平台配置（监听群/好友、admin、`logMessages` 消息日志开关）                                        |
| `agent.jsonc`                           | 回合时限、上下文预算与历史窗口、时间注入（`contextTokenBudget` / `contextConversationRetainCount` 等）    |
| `embedding.jsonc` / `memory.jsonc`      | 向量检索、embedding（`memory.jsonc` 只剩 `sqliteVec`：扩展路径 + 向量维度，改动需重启）                  |
| `log.jsonc`                             | 日志级别 / 分类 / 终端输出 / 热库（`hot`）与冷文件（`archive`）（热加载）                                |
| `security.jsonc`                        | Hub 入口治理（限流 / 帧上限 / 文本上限 / 严格模式）                                                      |
| `database.jsonc`                        | `databasePath`（game.db 路径；早先叫 `world.jsonc`——世界状态早已不在配置里，**文件名不影响加载**）        |

**每个键都必须有消费者**：零读取消费者的配置段已删除（`executors` / `timeouts` / `resilience` /
`lifecycle` / `inter-agent` / `plugin` 六段与 `memoryRetrieval` / `memoryStore`、`security` 段旧键、`agent` 段两个超时键、
`log.categories` 里 9 个来自旧链路的分类名）。
这些段曾是"假旋钮"（改了不生效），对应功能落地时**连同实现一起**加回。

**双向一致性由测试守护**（`tests/openLuo.Tests/Config/ConfigExampleSurfaceTests.cs`，3 例）：示例里**不允许**出现模型没有的键
（过时键 / 凭空键）；模型里每个字段都必须在示例里说明过（密钥类字段允许以注释形式给出）；`log.categories` 的名字必须来自
「代码实际会打日志的分类」白名单。新增配置键或改日志分类时，这个测试会先红。

配置热加载（`RuntimeConfigCenter` 文件监听）。**密钥（API key 等）只在 example 中留空占位，真实值仅存在于 `publish/linux-x64/config/`（gitignore 保护，不进 git）**。

**Hub 入口治理参数**在 `security.jsonc` 的 `security` 段（`hubEnabled` / `hubRequestsPerMinute` / `hubBurstLimit` / `hubMaxInputLength` / `hubMaxFrameBytes` / `hubStrictInputValidation`）：
默认宽松但真实生效（600 次/分钟、60 次/10 秒突发、文本 ≤32768 字符、单帧 ≤1 MiB），改后热加载——详见 [`docs/architecture/hub-protocol.md`](docs/architecture/hub-protocol.md) §4.9。

**上下文预算**见 [`docs/architecture/context-budget.md`](docs/architecture/context-budget.md)：token 上限、保底/可裁区域、
被裁明细（`Budget.DroppedContributions`）、历史窗口裁剪与客户端预算覆盖的服务端夹取。

## 6. 日志

双格式设计（`openLuo.Infrastructure.Logging.GameLogger`）：

- **文件**：JSONL，按分类分文件 `log/core/{category}.jsonl`（插件 `log/plugin/{id}.jsonl`），每行 `{ts, level, module, source, msg, data}`
- **终端**：`outputToConsole: true` 时输出 1+N 行文本 `[ts] [level] [source 文件:行号] [module]` + 内容行（level 带 ANSI 颜色，与启动期 .NET SimpleConsole 观感统一）；`ts` 与文件共用同一时间戳

其他：

- `log.jsonc` 热加载：改 `level` / `categories` 即时生效
- 启动完成日志：`Startup complete: 6/6 MCP server(s) connected, 6 extension(s) loaded, 21927 ms`
- QQ 消息收发日志（`[recv]`/`[send]`）按 `qqbot.jsonc` 的 `logMessages` 控制
- 插件日志走 `Plugin()` 隔离写入 `log/plugin/`

## 7. 构建与发布

常用目标（`Makefile`）：

```bash
make run            # 服务端本地运行（dotnet run --project openLuo -- --serve）
make run-playground # Playground 演示
make test           # 全量测试（slnx）
make test-kernel    # 内核测试（Capabilities + AgentContext）
make test-e2e       # E2E 测试
make build          # Release 构建（= build-server + build-clients）
make build-server   # 只构建服务端（内核 + Hub）
make build-clients  # 只构建四个客户端（openluo-cli/tui/gui/qq）
make publish        # 生产发布：n+1 份独立程序 → publish/linux-x64/
make publish-server # 只发布服务端
make publish-clients  # 只发布四个客户端
make publish-fast   # 目录形态发布（无单文件打包，迭代用）
make clean          # 清理（白名单清空发布目录，保留 build.sh/config/game.db）
```

`make publish` 流程：扩展 DLL 增量构建（无改动 0s）→ 服务端与 4 个客户端**各自** `dotnet publish` 到 tmpfs 空目录（~12s/个，绕开非空目录的 36s 黑盒清理）→ 白名单清空 `publish/linux-x64`（`KEEP_ENTRIES = build.sh config game.db` 及 `game.db-{journal,wal,shm}`，保留生产密钥/数据库/自定义脚本，其余同步为最新）→ 组装 data/native/mcp/extensions + 5 个可执行。

发布目录是**平铺的 n+1 份独立程序**，共享同一份 `config/`：
`openLuo`（服务端，含 `extensions/`）+ `openluo-cli` / `openluo-tui` / `openluo-gui` / `openluo-qq`（客户端，各自自包含单文件）。
多机部署时把 `openLuo` 放服务端，客户端程序放到任意机器、用 `OPENLUO_HUB_URL` 指向 Hub 即可（客户端不需要内核、不需要 `extensions/`）。

## 8. 测试

**10 个测试工程、441 全绿**（0 失败），全部位于 `tests/` 下：

| 工程                            | 数量 | 说明 |
| ------------------------------- | ---- | ---- |
| `openLuo.Tests`                 | 150  | 应用层/内核装配（配置、日志、会话、安全、QQ 桥逻辑、状态持久化、**LLM 参数真值 7 例**、**回合预算夹取 5 例**、**配置示例双向一致性 3 例**、**记忆用户维度 4 例**、**技能/工作流能力与目录 8 例**、**技能提示注入 5 例**、**文件化技能 14 例**、**技能引用方案 9 例**） |
| `openLuo.Capabilities.Tests`    | 58   | 能力目录、决策循环、调度、状态事务 |
| `openLuo.E2E.Tests`             | 67   | 端到端 + **架构边界 8 例** + **协议表面/多端 7 例**（状态投影、回执转发、回合取消、presence 过滤、多端共会话）+ **入口治理 7 例**（限流、帧上限、id 白名单、文本上限、严格模式）+ **枚举对齐 4 例**（内核枚举 ↔ Protocol 常量、删除响应形状）+ **HTTP 面 5 例**（REST 形状、路径常量、状态码映射） |
| `openLuo.Extensions.Tests`      | 48   | 扩展宿主与 6 个扩展（含记忆能力用户维度 4 例、人格档案版本探针 3 例） |
| `openLuo.Protocol.Tests`        | 31   | 信封、消息类型、错误码 |
| `openLuo.AgentContext.Tests`    | 46   | 上下文装配 + **预算与保留策略** + **域键与归因** + **全局块缓存**（版本探针/TTL 兜底/失效/跨角色不共享/引用而非复制） |
| `openLuo.Persistence.Tests`     | 12   | 持久化（`game.db`） |
| `openLuo.Logging.Tests`         | 8    | 日志（热库/冷文件/生命周期） |
| `openLuo.Assets.Tests`          | 7    | 资产存储 |
| `openLuo.Cli.Tests`             | 14   | CLI/TUI/GUI 客户端 + QQ 桥（重连韧性、投递回执、**说话人身份 7 例**含真实二进制实测） |

> 边界与"表面一致性"断言都在 `openLuo.E2E.Tests`：`ArchitectureBoundaryTests`（程序集引用边，8 例）与
> `HubProtocolSurfaceTests`（命令/事件必须 ∈ 已实现 ∪ 预留清单，7 例）。
> 分层图、依赖边清单与技术债见 [`docs/architecture/layers.md`](docs/architecture/layers.md)；
> 上下文预算的语义与验证方式见 [`docs/architecture/context-budget.md`](docs/architecture/context-budget.md)。

## 9. 快速开始

### 9.1 环境准备

- .NET SDK 10（`net10.0`）
- Python 3（用于运行插件进程）
- 可用的 LLM API Key（在 `config/llm.jsonc` 中配置）

### 9.2 本地启动

```bash
# 1) 依赖恢复
dotnet restore

# 2) 准备配置（首次）
mkdir -p config
cp openLuo/data/config/*.example.jsonc config/
# 将需要的 .example.jsonc 重命名为 .jsonc 并编辑，至少填写 llm.apiKey
# 例如：cp config/llm.example.jsonc config/llm.jsonc && 编辑 config/llm.jsonc

# 3) 启动服务端（Hub），另开终端启动任选客户端
make run                            # 服务端（= dotnet run --project openLuo -- --serve）
dotnet run --project openLuo.Cli    # CLI 客户端
dotnet run --project openLuo.Tui    # TUI 客户端
dotnet run --project openLuo.Qqbot  # QQ bot 客户端（生产主入口，需 config/qqbot.jsonc）
dotnet run --project openLuo.Gui    # GUI 客户端（Avalonia）
```

> 客户端与服务端是**独立进程**：客户端不含内核，只经 `OPENLUO_HUB_URL`（默认 `ws://127.0.0.1:8674/v1/stream`）
> 连到 Hub。旧的多入口用法（`./openLuo --qq` 等）已移除，服务端会明确报错并提示对应的客户端程序。
>
> **多端共享同一段对话**：客户端会话由 `OPENLUO_SUBJECT_ID` / `OPENLUO_AGENT_ID` / `OPENLUO_CONVERSATION_ID` 决定；
> 与 QQ 桥配置（`config/qqbot.jsonc` 的 `defaultSubjectId` / `defaultAgentId` / `privateConversationId`）填同一组值时，
> **PC 客户端与手机 QQ 私聊就共享同一段对话上下文**（Hub 按 `(subjectId, agentId, conversationId)` 复用会话，
> 两端都能看到彼此的消息，回合按会话串行）；不填 conversation 仍每次新建，不同 conversation 之间互不影响。
>
> **说话人身份**（PC 三端 CLI / TUI / GUI 均支持，**可选**）：
> `OPENLUO_USER_ID` = 说话人稳定身份（平台用户 id）。填上后 Hub 会把回合归因到这个身份（落库 `speaker_id`，
> 并作为记忆的 User 域分区键）——**多人共用一台机器 / 一个客户端多账号时用它**才能把回合分清是谁说的；
> 与 QQ 桥自动填的 `UserId`（QQ 号）是同一个字段。`OPENLUO_THREAD_ID` = 会话内线程 id（同一会话里并行话题）。
> **两者不设 = 不上送该字段，行为与从前完全一致**（归因回退到会话主体）。

### 9.3 生产部署

```bash
make publish                                     # 产物 → publish/linux-x64/（1 服务端 + 4 客户端）
cd publish/linux-x64 && ./openLuo --serve        # 启动服务端
cd publish/linux-x64 && ./openluo-qq             # 另开终端启动 QQ bot（读 config/qqbot.jsonc）
```

发布目录白名单保留 `build.sh` / `config/`（密钥）/ `game.db`，其余每次同步为最新产物；部署需拷贝整个 `publish/linux-x64/` 目录（含独立 native 库与 `extensions/`）。

## 10. Roadmap

### 已完成

- [x] 通用 Agent 内核（能力目录 / 决策循环 / 并行调度 / 状态事务 / 上下文快照 / 输出队列）
- [x] 领域扩展宿主与 **6 个扩展**（memory / companion / world / party / sticker / music）
- [x] MCP / A2A 远程能力接入（含 12306 / 高德地图 / mcd / web-fetch 等 server），MCP 失败语义（`isError`）贯通
- [x] QQ bot 生产入口（消息收发、多模态分段、`[recv]`/`[send]` 日志、断线自愈）
- [x] Inter-Agent 最小协议（`ask_character`）
- [x] 日志（双格式 + 热库/冷文件 + 检索端点 + 14 天/20 万保留）、观测（`/v1/metrics` / `/v1/traces`）、审计事件
- [x] **中心 Hub + 瘦客户端**：Wire 协议 v1、鉴权（token/角色/admin 前缀）、会话持久化、资产（assetRef）、调度（cron）已落地、**作业无处理器**（零 `IJobHandler`，见 [`docs/todos.md`](docs/todos.md) T6）、高危能力确认握手
- [x] **多端上下文**：会话复用（多端共享同一段对话）、世界状态投影与持久化、投递回执、回合取消、presence 过滤
- [x] **入口治理**：鉴权前限流 + 输入校验（HTTP/WS 同一套策略，`security.jsonc` 热加载，错误码 `8001` / `4003` / `5004` / `2002`）
- [x] **工程结构**：服务端/客户端拆分（n+1 独立程序）、抽 `openLuo.Application`、依赖收敛、`ArchitectureBoundaryTests` 守边界、测试归位 `tests/`、playground 清瘦且不再依赖宿主 exe
- [x] **内核优化步 0（预算 + 真值轴）**：上下文预算成真（token 计数口径、保底区域不裁 + 被裁逐条记账、历史纳入预算）、配置真值化（删除 6 个零消费配置段与 8 个假键、接上 4 个真键、LLM route 的 temperature/maxTokens 不再被硬编码顶掉）、回合预算上 wire 并由服务端夹取、game.db 死 schema 清渣（`DatabaseInitializer` 954 → 459 行）、**旧 python 插件宿主遗骸删除**（`openLuo/data/plugins`，73 文件；其能力已由 `extensions/` 的 C# 扩展与 `mcp/` 的 MCP server 取代）
- [x] **技能与工作流接线**：目录注入（`[Skills]`/`[Workflows]`/`[RemoteAgents]`）+ 按需使用（`core:load_skill` 取正文、`core:run_workflow` 黑盒编排）+ 文件化技能（`data/skills/*.md`，加技能不用重编译）+ 工作流节点处理器注册入口（目录与登记已接线；**调用未绑定 invoker**，端到端尚不可用，见 [`docs/todos.md`](docs/todos.md) D1）
- [x] 构建与发布（`make publish` 平铺 n+1 份 + 白名单保护 + 构建加速）

### 未完成

- [ ] Hub ↔ Hub 联邦（协议已定，见 hub-protocol §10）
- [ ] 上下文**空间模型**（四域已落地，剩余空间/可见性一半；交接见 [`context-scope-risk.md`](docs/architecture/context-scope-risk.md) L10–L11 与 **完整设计稿** [`docs/architecture/context-spaces.md`](docs/architecture/context-spaces.md)——agent/user/group 三类空间、制品归属与方向性可见性、发布与摘要、权限分层与主体确认、推理任务下发）
- [ ] 插件视角世界状态与内核状态事务的键控统一（唯一登记：[`docs/todos.md`](docs/todos.md) T3；归属 [`runtime-modules.md`](docs/architecture/runtime-modules.md) §3）
- [ ] 玩家主对话 / 角色间 backchannel 线程隔离（空间模型下 `Thread` 是空间内的子隔离，见 [`context-spaces.md`](docs/architecture/context-spaces.md) §3.2；登记 [`docs/todos.md`](docs/todos.md) T7）
- [ ] 角色自主多 Agent 协作增强（`delegate_character_task` / `consult_party`；委托的**继承受限视角**与递归上限见 [`context-spaces.md`](docs/architecture/context-spaces.md) §5.2/§5.6；登记 [`docs/todos.md`](docs/todos.md) T9 与 [`workers-and-hooks.md`](docs/architecture/workers-and-hooks.md) W4）
- [ ] 语音与多模态：TTS 已以 **MCP 工具外挂**（Qwen-TTS）；客户端侧语音播放、Live2D 驱动、VL 视觉记忆未做
- [ ] ComfyUI CG 生成管线
- [ ] 安全治理（限流/输入校验见 hub-protocol，此处不重复；仍缺扩展权限强制、按 subject/租户的细分配额、审计覆盖面——唯一登记 [`docs/todos.md`](docs/todos.md) T14，设计见 [`runtime-modules.md`](docs/architecture/runtime-modules.md) §3/§3.1/§4）
- [ ] 观测回放前端（轨迹已入库，回放可视化未做；与 [`log-store.md`](docs/architecture/log-store.md) §9 相关，**未在 [`docs/todos.md`](docs/todos.md) 登记**）

## 11. 贡献规范

### 11.1 基本流程

1. Fork / 创建分支（`feature/*`、`fix/*`、`refactor/*`）
2. 本地开发并补充测试
3. 运行 `make format && make test`
4. 提交 PR，关联对应 Issue

### 11.2 Issue 模板（建议）

```md
## 类型
- [ ] Bug
- [ ] Feature
- [ ] Refactor
- [ ] Docs

## 背景 / 目标
一句话描述问题或目标。

## 复现步骤（Bug 必填）
1.
2.
3.

## 期望行为

## 实际行为

## 日志 / 截图 / 附加信息
```

### 11.3 PR 模板（建议）

```md
## 变更摘要

## 关联 Issue
Closes #

## 变更类型
- [ ] Feature
- [ ] Bugfix
- [ ] Refactor
- [ ] Docs
- [ ] Test

## 自测清单
- [ ] 已执行 `make format`
- [ ] 已执行 `make test`
- [ ] 已更新相关文档（如适用）
```

欢迎通过 Issue / PR 提交功能建议、插件方案或路线图优化建议。
