# openLuo（开源洛天依）

> 项目名称统一为 **openLuo**。

---

## 1. 项目简介

**openLuo** 是一个开源的“可扩展 AI 角色引擎”项目，目标是构建可持续演进的洛天依式交互体验底座：

- 支持 **CLI / TUI** 双入口运行；
- 以 **插件化（JSON-RPC over stdio）** 作为核心扩展机制；
- 内置状态系统（State）、资产系统（Asset）、时间线系统（Timeline）与记忆系统（RAG）；
- 面向后续 GUI、语音、Live2D、多模态与多 Agent 协作能力持续演进。

---

## 2. Roadmap（按当前代码基线）

### 2.1 已完成（Foundation）

- [x] 核心运行时：CLI / TUI 双入口
- [x] 命令系统插件化：动态命令注册与路由
- [x] 动态帮助中心：命令 / 工具 / 子代理统一注册与展示
- [x] Hook 机制（onStartup / onChatBefore / onNarrativeAfter / onScheduleDue / onStatusQuery 等）
- [x] 反向 `game/*` API（state / asset / timeline / narrative / memory / commands 等）
- [x] 状态系统（State）与规则化结算链路
- [x] 资产系统（Asset）与解锁/关联能力
- [x] 时间系统（virtual / realtime / disabled 三模式）
- [x] Timeline 事件调度（create/query/poll_due/ack/cancel）
- [x] RAG 记忆检索（sqlite-vec + 回退策略）
- [x] 自动化测试基线（当前 263 tests 通过）

### 2.2 已完成（Agent 基线）

- [x] `Executor` 基线：plan / response / state-update / memory-recall 等结构化执行器
- [x] 角色 turn 主链：`CharacterAgent -> CharacterTurnCoordinator -> Character*Stage`
- [x] `cosplay skill` 注入层：角色人格从旧 kernel prompt 中剥离
- [x] Chat 模式高风险能力确认握手
- [x] ToolResult continuation loop：确认执行后可继续 agent 循环
- [x] Companion 命令集：`/chat`、`/task`、`/task_status`、`/characters`、`/switch`
- [x] 玩家显式多角色协作基础链路：`/task` 可分发并汇总结果

### 2.3 下一主线（Architecture Next）

- [x] 统一 Agent Capability Registry（第一阶段基线）
- [x] 角色间真实通信协议（最小 `ask_character` + `AgentAsk / AgentReply`）
- [x] 角色 runtime 预热已接入启动与主入口（仍待独立 runtime manager 收口）
- [x] `/chat` 单一路径：旧叙事插件已降级为 agent 可调用 renderer
- [ ] 玩家主对话 / 角色间 backchannel / 任务线程隔离
- [ ] 角色自主多 Agent 协作增强（`delegate_character_task` / `consult_party`）

### 2.4 后续扩展（Product）

- [ ] GUI（桌面客户端）
- [ ] 机器人接入（插件化：QQ / Discord / Telegram 等）
- [ ] TTS 语音合成（可插拔供应商）
- [ ] Live2D 驱动（口型 / 表情 / 动作联动）
- [ ] ComfyUI CG 生成管线
- [ ] VL 模型多模态（看图理解、视觉记忆）
- [ ] 观测与回放（trace / replay / 调度可解释性）
- [ ] 安全与权限治理（插件权限强制、审计、限流）

---

## 3. 已完成项目的实现情况

### 3.1 核心引擎与命令系统

- `GameEngine` 已实现“解析命令 -> 命中插件命令 -> 执行 -> 回写结果”主链路。
- 命令前后拦截由 `CommandGate` 提供，支持时间窗限制、到期事件处理、执行后 hook。

### 3.2 插件协议与调度

- `McpPluginHost` + `PluginProcess` 已实现插件加载与 JSON-RPC 通信；
- 插件通过 `tools/list` 暴露命令，通过 `tools/call` / `hooks/call` 执行；
- 插件可反向调用宿主 `game/*` API 完成状态查询、剧情推进、记忆写入等。

### 3.3 状态 / 资产 / 时间线

- 已完成 `game/state/*`、`game/asset/*`、`game/timeline/*` 的核心闭环；
- 支持状态注册、变更校验、范围约束、日志化；
- 支持资产入库、blob/meta、实体关联、解锁记录；
- Timeline 支持到期轮询与 ack/cancel。

### 3.4 叙事与记忆

- 已实现多节拍叙事链（`run_chain`）与命令建议执行链路；
- 已实现资源结算协调器（一次 LLM 评估 + 规则校验 + 应用）；
- RAG 记忆系统接入 sqlite-vec，并有降级回退策略。

### 3.5 质量保障

- 测试覆盖单元 / 集成场景，当前基线为 **263 通过、0 失败**。

### 3.6 Agent 与调度现状

- 旧 `AgentKernel` 已移除，角色主链已迁到 `CharacterAgent -> CharacterTurnCoordinator`。
- 已完成 `cosplay skill` 注入，角色人格不再直接硬编码在通用 system prompt 中。
- chat 模式确认握手与 continuation loop 正在迁移到 executor 驱动的工具阶段。
- 已完成玩家显式多角色协作入口 `/task`。
- 已完成统一 capability registry 第一阶段基线，agent 能同时看到插件能力、核心能力与结构化角色 roster。
- 已完成 `ask_character` 的最小 inter-agent 能力，以及 `AgentAsk / AgentReply` 消息类型。
- 已完成 `/chat` 单一路径，旧叙事聊天插件不再由 orchestrator 外层直接回退调用。
- 已完成 CLI/TUI 启动与主入口的 runtime 预热接入。

当前明确**尚未完成**的能力：

- 玩家主会话与角色间 backchannel 的正式线程隔离。
- `delegate_character_task` / `consult_party` 等更高层协作能力。

---

## 4. 未完成项目的实现方案计划

> 目标：先补齐“角色间通信与 runtime 架构”，再扩展交互通道，最后做视觉与沉浸。

### 阶段 P1：Inter-Agent Runtime 重构（最高优先级）

1. **统一 Capability Registry**
   - 方案：以单一 `CapabilityDescriptor` 替代“插件命令桥 + companion catalog”分裂的能力面
   - 交付：Agent 能同时看到插件工具、核心 companion 能力、内建 inter-agent 能力
2. **角色间消息协议**
   - 方案：新增 `AgentAsk / AgentReply / AgentDelegate / FinalReply` 等消息类型
   - 交付：角色可真实向另一角色询问、委托、回收结果
3. **Runtime 预热**
   - 方案：游戏开始或读档后统一启动全部启用角色 runtime
   - 交付：角色网络常驻在线，而不是按需冷启动
4. **`/chat` 单一路径**
   - 方案：agent 负责聊天主逻辑，叙事系统改为渲染能力
   - 交付：`/chat` 不再回落到旧叙事插件主流程

> 详细计划见：`design/todo/inter-agent-runtime-refactor-plan.md`

### 阶段 P2：交互通道扩展

1. **GUI**
   - 方案：在现有内核之上增加桌面前端（状态面板、剧情区、命令输入区）
2. **机器人接入（插件化）**
   - 方案：把消息平台适配层做成插件，统一映射到命令与事件总线
3. **TTS 语音合成**
   - 方案：抽象 `ITtsProvider`，支持多厂商与本地模型，接入角色说话事件

### 阶段 P3：视觉与多模态能力

1. **ComfyUI CG 生成**
   - 方案：将叙事事件映射到 ComfyUI workflow，资产自动入库并关联剧情上下文
2. **VL 模型多模态**
   - 方案：新增视觉输入通道（截图/图片），进入记忆与状态评估链
3. **Live2D**
   - 方案：结合 TTS 音素/情绪信号驱动口型与表情，完成角色实时演出

### 阶段 P4：工程化完善（并行推进）

- 安全治理：插件权限强制、调用审计、速率限制、敏感能力审批
- 可观测性：trace、replay、性能指标（P95/P99）与错误分类
- 开发体验：模板化插件脚手架、文档自动对齐、回归测试矩阵

---

## 5. 建议的里程碑节奏（可调整）

- **v0.2**：Inter-Agent Runtime 基线（capability registry + ask_character + runtime 预热）
- **v0.3**：`/chat` 单一路径 + 角色自主协作增强
- **v0.4**：GUI Alpha + Bot 插件接入
- **v0.5**：TTS Beta + ComfyUI CG 管线
- **v0.6**：VL 多模态 + Live2D Alpha
- **v1.0**：稳定版（完整安全治理 + 观测回放 + 完整文档）

---

## 6. 快速开始（Quick Start）

### 6.1 环境准备

- .NET SDK 10（`net10.0`）
- Python 3（用于运行插件进程）
- 可用的 LLM API Key（在 `config/llm.jsonc` 中配置）

### 6.2 本地启动

```bash
# 1) 依赖恢复
dotnet restore

# 2) 准备配置（首次）
mkdir -p config
cp openLuo/data/config/*.example.jsonc config/
# 将需要自定义的 .example.jsonc 重命名为 .jsonc 并编辑，至少填写 llm.apiKey
# 例如：cp config/llm.example.jsonc config/llm.jsonc && 编辑 config/llm.jsonc

# 3) CLI 模式运行
make run
```

> 如需 TUI 模式：

```bash
dotnet run --project openLuo -- --tui
```

### 6.3 常用开发命令

```bash
make test      # 运行测试
make format    # C# + Python 格式化
make build     # Release 构建
make publish   # 发布 linux-x64 / win-x64
```

---

## 7. 架构文档导航

建议按以下顺序阅读：

1. `design/technical/architecture.md`（当前实现总览）
2. `design/technical/architecture-analysis.md`（代码对齐分析）
3. `design/technical/architecture.dot`（Graphviz 结构图）
4. `design/plugin/mcp-protocol.md`（协议细节）
5. `design/plugin/plugin-spec.md`（插件规范）
6. `design/plugin/plugin-dev-guide.md`（插件开发入门）
7. `design/gameplay/commands-reference.md`（命令参考）
8. `design/todo/inter-agent-runtime-refactor-plan.md`（当前主线重构计划）

---

## 8. 贡献规范（Issue / PR 模板）

### 8.1 基本流程

1. Fork / 创建分支（`feature/*`、`fix/*`、`refactor/*`）
2. 本地开发并补充测试
3. 运行 `make format && make test`
4. 提交 PR，关联对应 Issue

### 8.2 Issue 模板（建议）

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

### 8.3 PR 模板（建议）

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
- [ ] 已补充/更新测试（如适用）

## 风险与回滚
- 风险点：
- 回滚方案：
```

> 当前仓库尚未内置 `.github` 模板文件；可先按上述模板在 Issue / PR 描述中手工填写，后续可落地为模板文件。

---

## 9. 说明

- 本 README 的 roadmap 为当前主线规划，会随实现进度持续更新。
- 欢迎通过 Issue / PR 提交功能建议、插件方案或路线图优化建议。
