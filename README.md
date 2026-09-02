# openLuo（开源洛天依）

> 项目名称统一为 **openLuo**。

---

## 1. 项目简介

**openLuo** 是一个开源的"可扩展 AI 角色引擎"，目标是构建可持续演进的洛天依式虚拟角色交互体验底座。项目以"通用 Agent 内核 + 领域扩展"为架构核心：内核不绑定任何业务（RPG、桌宠还是聊天陪伴由加载的扩展决定），将 LLM 推理、能力调度、状态管理、记忆检索、多角色协作等能力解耦为独立模块，支持 CLI / TUI / QQ / GUI 四入口运行。

QQ bot 是当前生产主入口（`./openLuo --qq`），通过 Milky WebSocket/HTTP API 对接 QQ 消息。

## 2. 技术栈

| 层级          | 技术选型                                                          |
| ------------- | ----------------------------------------------------------------- |
| 语言 / 运行时 | C# 13 / .NET 10                                                   |
| AI / LLM 抽象 | `Microsoft.Extensions.AI`（OpenAI provider，路由多模型）          |
| 向量检索      | SQLite + Dapper + `sqlite-vec`                                    |
| 图形渲染      | SkiaSharp                                                         |
| 终端 UI       | Terminal.Gui（CLI / TUI）                                         |
| 桌面 GUI      | Avalonia                                                          |
| 远程能力      | MCP 官方 SDK（ModelContextProtocol 2.1.0）、A2A（1.0.0-preview2） |
| 测试          | xUnit / NSubstitute；内核、扩展、CLI、E2E 分工程                  |

## 3. 架构：通用 Agent 内核 + 领域扩展

| 层级     | 工程                                                            | 职责                                                         |
| -------- | --------------------------------------------------------------- | ------------------------------------------------------------ |
| 基础     | `openLuo.Foundation` / `openLuo.Llm` / `openLuo.Memory`         | 基础设施、LLM、Embedding、记忆端口与实现                     |
| 内核契约 | `openLuo.Capabilities` / `openLuo.AgentContext`                 | 能力目录、决策循环、并行调度、状态事务、上下文快照、输出队列 |
| 桥接     | `openLuo.Capabilities.Llm` / `.Mcp` / `.A2A`                    | LLM 原生 tool calls、MCP、Agent2Agent 远程能力               |
| 扩展宿主 | `openLuo.Extensions.Host`                                       | manifest、依赖拓扑、程序集加载、失败隔离                     |
| 领域扩展 | `extensions/{memory,companion,world,party}`                     | 记忆、伴侣人格、世界状态、多角色；每个扩展自带 manifest      |
| 平台     | `openLuo.Cli` / `openLuo.Tui` / `openLuo.Gui` / `openLuo.Qqbot` | 输入解析、输出渲染与平台传输                                 |
| 宿主     | `openLuo`                                                       | 组合根、配置加载、入口分发                                   |
| Demo     | `openLuo.playgraound`（程序集名 `openLuo.Playground`）          | 新内核能力循环最小可运行演示                                 |

架构约定：

- 内核零业务配置硬编码；领域数据随扩展走
- 扩展注册的 `canonicalId` 自动命名空间化为 `<extension-id>:<local-id>`；`core:` 保留给内核
- 领域扩展目录以 `.disable` 结尾时完全跳过
- `openLuo/Modules/` 保留 `AppShell`（配置加载）/ `WorldState` / `GameBridge` 等基础能力；业务宿主链路已迁移至新内核

## 4. 能力与协议

- **MCP**：`openLuo.Capabilities.Mcp` 使用官方 `ModelContextProtocol`，支持 stdio / http / streamable-http 三种传输，per-server 请求头（含 `{env:VAR}` 占位展开）；连接失败结构化降级（server 标记不可用，不阻塞宿主）。server 列表见 `config/mcp-servers.jsonc`。
- **A2A**：`openLuo.Capabilities.A2A` 通过 Agent Card 发现 skills 并映射为 `RemoteAgent` 能力。
- **原生 tool calls**：LLM 桥接只把无 tool-call 非空文本视为最终回复；tool-call 结果进入决策循环继续规划。
- **工具调度**：`DefaultCapabilityDispatcher` 并行调度 + 决策循环（预算/终止条件/非法并行拒绝），调度日志归 `agent/dispatch`（start / ok / failed / batch done）。
- **Inter-Agent**：最小 `ask_character` + `AgentAsk / AgentReply` 消息协议，角色间真实通信。
- **记忆 / 状态 / 时间线 / 资产**：RAG 记忆检索（sqlite-vec + 降级回退）、状态事务（mutation 冲突检测）、Timeline 事件调度、资产与解锁。

## 5. 配置

运行时从 `./config/` 加载 `{name}.jsonc`（模板在 `openLuo/data/config/*.example.jsonc`，共 15 个）：

| 配置                                                                                                                                | 说明                                                              |
| ----------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| `llm.jsonc`                                                                                                                         | LLM 路由（多 route：模型/供应商/API key/启用开关）                |
| `mcp-servers.jsonc`                                                                                                                 | MCP server 列表（传输、URL/command、per-server 请求头）           |
| `qqbot.jsonc`                                                                                                                       | QQ bot 平台配置（监听群/好友、admin、`logMessages` 消息日志开关） |
| `agent.jsonc`                                                                                                                       | 运行时参数（chat 轮询超时、上下文保留轮数、时间注入）             |
| `executors.jsonc`                                                                                                                   | 各执行器温度/最大 token                                           |
| `embedding.jsonc` / `memory.jsonc`                                                                                                  | 向量检索与记忆                                                    |
| `log.jsonc`                                                                                                                         | 日志级别 / 分类 / 终端输出开关（热加载）                          |
| `timeouts.jsonc` / `resilience.jsonc` / `security.jsonc` / `lifecycle.jsonc` / `inter-agent.jsonc` / `world.jsonc` / `plugin.jsonc` | 超时、重试、安全、生命周期、角色间、世界、插件策略                |

配置热加载（`RuntimeConfigCenter` 文件监听）。**密钥（API key 等）只在 example 中留空占位，真实值仅存在于 `publish/linux-x64/config/`（gitignore 保护，不进 git）**。

## 6. 日志

双格式设计（`openLuo.Infrastructure.Logging.GameLogger`）：

- **文件**：JSONL，按分类分文件 `log/core/{category}.jsonl`（插件 `log/plugin/{id}.jsonl`），每行 `{ts, level, module, source, msg, data}`
- **终端**：`outputToConsole: true` 时输出 1+N 行文本 `[ts] [level] [source 文件:行号] [module]` + 内容行（level 带 ANSI 颜色，与启动期 .NET SimpleConsole 观感统一）；`ts` 与文件共用同一时间戳

其他：

- `log.jsonc` 热加载：改 `level` / `categories` 即时生效
- 启动完成日志：`Startup complete: 6/6 MCP server(s) connected, 4 extension(s) loaded, 21927 ms`
- QQ 消息收发日志（`[recv]`/`[send]`）按 `qqbot.jsonc` 的 `logMessages` 控制
- 插件日志走 `Plugin()` 隔离写入 `log/plugin/`

## 7. 构建与发布

常用目标（`Makefile`）：

```bash
make run            # CLI 模式运行（dotnet run --project openLuo）
make run-playground # Playground 演示
make test           # 全量测试（slnx）
make test-kernel    # 内核测试（Capabilities + AgentContext）
make test-e2e       # E2E 测试
make build          # Release 构建（slnx）
make publish        # 生产发布 linux-x64（单文件 + native 独立）
make publish-fast   # 目录形态发布（无单文件打包，迭代用）
make clean          # 清理（白名单清空发布目录，保留 build.sh/config/game.db）
```

`make publish` 流程：扩展 DLL 增量构建（无改动 0s）→ `dotnet publish` 到 tmpfs 空目录（~12s，绕开非空目录的 36s 黑盒清理）→ 白名单清空 `publish/linux-x64`（`KEEP_ENTRIES = build.sh config game.db`，保留生产密钥/数据库/自定义脚本，其余同步为最新）→ 组装 data/native/mcp/extensions。全程 ~13-34s，目录 inode 保持不变。

## 8. 测试

5 个测试工程，**127 全绿**（0 失败）：

| 工程                         | 数量 |
| ---------------------------- | ---- |
| `openLuo.Tests`              | 52   |
| `openLuo.Capabilities.Tests` | 28   |
| `openLuo.E2E.Tests`          | 25   |
| `openLuo.AgentContext.Tests` | 13   |
| `openLuo.Extensions.Tests`   | 6    |

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

# 3) 启动（四入口之一）
make run                    # CLI（默认）
dotnet run --project openLuo -- --tui   # TUI
dotnet run --project openLuo -- --qq    # QQ bot（生产主入口，需 qqbot.jsonc）
dotnet run --project openLuo -- --gui   # GUI（Avalonia）
```

> `--cli / --tui / --qq / --gui` 互斥，只能选一个。

### 9.3 生产部署

```bash
make publish                              # 产物 → publish/linux-x64/
cd publish/linux-x64 && ./openLuo --qq    # 启动 QQ bot
```

发布目录白名单保留 `build.sh` / `config/`（密钥）/ `game.db`，其余每次同步为最新产物；部署需拷贝整个 `publish/linux-x64/` 目录（含独立 native 库与 `extensions/`）。

## 10. Roadmap

### 已完成

- [x] 通用 Agent 内核（能力目录 / 决策循环 / 并行调度 / 状态事务 / 上下文快照）
- [x] 领域扩展宿主与 4 个扩展（memory / companion / world / party）
- [x] MCP / A2A 远程能力接入（含 12306 / 高德地图 / mcd / web-fetch 等 server）
- [x] QQ bot 生产入口（消息收发、多模态分段、`[recv]`/`[send]` 日志）
- [x] Inter-Agent 最小协议（`ask_character`）
- [x] 工具调度日志、启动完成日志、日志双格式统一
- [x] 构建加速（47-57s → 13-34s）、发布白名单保护

### 未完成

- [ ] 玩家主对话 / 角色间 backchannel 线程隔离
- [ ] 角色自主多 Agent 协作增强（`delegate_character_task` / `consult_party`）
- [ ] TTS 语音合成、Live2D 驱动、VL 多模态（看图理解、视觉记忆）
- [ ] ComfyUI CG 生成管线
- [ ] 安全治理（插件权限强制、审计、限流）与观测回放（trace / replay / 指标）

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
