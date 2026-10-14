# 证据索引：deepseek-harness @ `d7432673`

> **状态**：调研存档，**非规范**。本文件只保存**证据与来源**，语义结论见 [context-management.md](context-management.md)。
> **来源快照**：`/home/yoitsuholo/Code/deepseek-harness`，HEAD = `d7432673`（2026-10-09，`release-0.2.1-alpha.2`）。
> **纪律**：行号只对该提交有效；引用前先核对提交号。凡"文档声称"与"代码实际"冲突，**一律以代码为准**（冲突清单见 §4）。

---

## 1. 采集方式

| 项 | 内容 |
| --- | --- |
| 范围 | 上下文管理（装配 / 块序 / 压缩 / 外置 / 计量 / 时间上下文） |
| 方法 | 4 个只读侦察切片（压缩、外置与注入、计量与溢出恢复、块序与稳定性）并行取证；并直接通读其架构总览与 6 份子系统设计稿 |
| 未做 | **未运行其构建/测试/格式化**，未写入任何文件（两端仓库全程只读） |
| 通读的文档 | `docs/architecture.zh.md`；`docs/subsystems/` 的 `session` / `system-prompt` / `compaction` / `spill` / `token-meter` / `conversation` |
| 通读的决策笔记 | `implemented/architecture/`：`2026-09-02-system-prompt-as-surface-node`、`2026-07-24-separate-context-injection-from-turn-execution`、`2026-07-20-routed-model-context-and-compaction-policy`；`implemented/feature/`：`2026-09-02-in-history-system-prompt-replacement`、`2026-08-05-context-form-vocabulary`；`archived/architecture/`：`2026-08-25-sparse-first-party-prompt-section-orders`、`2026-08-11-trajectory-conversation-context-assembly`（见 §5 纠错）；`archived/feature/`：`2026-07-16-durable-per-step-time-context` |
| 未列入 A–R 的旁证 | `2026-07-08-agent-scope-contexts`（作用域=注册归属 + 最近层遮蔽）、`2026-07-08-tool-output-spill-files`（外置来源）、`2026-07-07-session-prefix`（已回退，见 A 的反面教材）、`proposed/feature/2026-07-06-recallable-compaction`（仍是提案） |

---

## 2. 条目 → 来源证据（路径相对来源仓库根）

### A 稳定头 + 易变只追加

| 证据 |
| --- |
| `.agents/notes/implemented/architecture/2026-09-02-system-prompt-as-surface-node.zh.md`（提示词 = surface 第 0 号节点；`EpochHeader = { config, adapterDefaults?, tools? }`；"协议请求不变：surface 折叠产出的就是序列化器发送的消息列表"） |
| `packages/core/agent-loop/src/runtime-context.ts:65-103`（`SystemPromptProjection`；`runtime-context.ts:114-160` 运行时上下文只跟踪 + 追加） |
| `docs/subsystems/system-prompt.zh.md`（`PromptContext` = "缓存安全结构"，作 user 角色快照；agent loop 只在完整快照变化或被压缩移除时记录） |
| `packages/context/time-context/README.zh.md`（"每次注入都是持久历史中额外的一条 user 角色消息"；"仅追加；新可见内容位于可复用请求前缀之后"） |
| `packages/spill/spill-policy/README.zh.md`（KV 缓存影响栏：仅追加） |
| `packages/core/agent-loop/README.zh.md:160,164`（in-history 路由上每个保留的提示词版本持续计费；前缀复用要求"此前消息与声明逐字节一致"） |
| `packages/core/agent-loop/src/agent.ts:416-434`（运行时上下文以 `surfaceOp:'append'` 提交） |

### B 中心分配表 + 稀疏编号 + 名称序决胜

| 证据 |
| --- |
| `packages/core/system-prompt/src/index.ts:131-175`（`SECTION_ORDERS` / `CONTEXT_ORDERS`；`HARNESS_IDENTITY:-1000` … `DEPLOYMENT_PERSONA_SUFFIX:10200`；`WORKING_DIRECTORY:100` / `SANDBOX_POLICY:110` / `APPROVAL_POLICY:115` / `SUBAGENT_DELEGATION:120`） |
| `packages/core/system-prompt/src/index.ts:238-246`（`compareNames`：注释"locale-independent, so the order is identical on every machine"；`comparePromptSections` = order 差 ‖ 名称代码单元序） |
| `packages/core/system-prompt/src/index.ts:490-501`（`getSectionOrder` / `getContextOrder`） |
| `packages/core/system-prompt/tests/system-prompt.spec.ts:152-158`（断言全为整数、互不重复、排序后相邻差 ≥10）、`:160-173,304`（把可复用段按**相反顺序**注册，断言顺序不变） |
| `.agents/notes/archived/architecture/2026-08-25-sparse-first-party-prompt-section-orders.zh.md`（问题段："主要工具序列连续占用 100 到 117，后续插入还使用半步数值"；"相同 order 依赖 JavaScript 稳定排序，使插件激活顺序成为实际的平局规则"） |
| `.agents/notes/archived/bug-fix/2026-08-24-system-prompt-section-order-ties.zh.md`（同号冲突的真实回归） |

### C 结构不可裁 vs 策略保留

| 证据 |
| --- |
| `packages/core/session/src/surface.ts:489-506`（`assertSystemHeadRewrite`：第 0 号节点只能被**恰好覆盖它**的 `system/message` 重写） |
| `packages/compaction/compaction-basic/src/region.ts:99-105,133-138`（`systemHead`；`firstIdx` 有系统头时从 1 开始 ⇒ 范围起点永远是第一个非系统节点） |
| `packages/core/system-prompt/src/index.ts:578-608`（`refreshContext`：required 贡献即使被抑制仍保留，并按 order 重新插回） |
| `.agents/notes/implemented/architecture/2026-09-02-system-prompt-as-surface-node.zh.md`（Consequences："第 0 号节点永不被压缩"由 surface 管理器在替换操作本身强制） |

### D 空块占位

| 证据 |
| --- |
| `packages/core/agent-loop/src/runtime-context.ts:88-93`（`head === undefined` 时无条件 append，`rendered` 可为 `''`） |
| `packages/core/session/src/surface.ts:134-139`（空 `system`/`developer` 节点保留 surface 位置、不产生 wire 消息） |
| `.agents/notes/implemented/architecture/2026-09-02-system-prompt-as-surface-node.zh.md`（"当初始渲染的提示词为空时，循环在初始接纳的用户消息之前预留空系统头部…省略该空节点会让后来的提示词追加在用户历史之后"） |

### E 压缩阈值与归属

| 证据 |
| --- |
| `packages/compaction/compaction-basic/src/config.ts:20,23`（`thresholdRatio` 0.8 / `headroomTokens` 65536）、`:75-79`（`retainRatio` 0.16 / `retainTokens` 互斥 / `maxTokens` 默认 = headroomTokens）、`:105-108`（`compactionRetries` 1 / `maxOverflowRetries` 1 / `auto` true）、`:172,191-198`（`resolveCompactSpec`：`thresholdTokens = floor(min(W × ratio, (W − O) − headroom))`；`retainTokens >= thresholdTokens` 加载期拒绝） |
| `packages/compaction/compaction-basic/src/index.ts:294-346`（主动压力检查重解析路由容量）、`:65-68`（`reservedCompletionTokens` = 生效信封 `maxTokens` ?? 适配器默认 ?? 0） |
| `.agents/notes/implemented/architecture/2026-07-20-routed-model-context-and-compaction-policy.zh.md`（容量归适配器、策略归消费者；容量不进 `EpochHeader`；`listModels()` 不是权威） |
| `packages/llm/llm-deepseek/src/defaults.ts:5-8`（`DEFAULT_MAX_TOKENS` 256000 / `DEFAULT_CONTEXT_WINDOW` 1000000） |

### F 先无模型剪枝，再决定摘要

| 证据 |
| --- |
| `packages/compaction/compaction-tool-result-pruner/src/config.ts:7,11-13`（`thresholdChars` 8192 / `headChars` 4096 / `tailChars` 1024） |
| `packages/compaction/compaction-tool-result-pruner/src/index.ts:88,136,160`（按 Unicode 码点切片；替换事件紧邻影子价事件） |
| `packages/compaction/compaction-tool-result-pruner/README.zh.md`（"头部加标记加尾部之和必须不超过阈值"；"修剪只在压缩触发条件满足后运行"；字素簇可能被拆分） |
| `packages/compaction/compaction-basic/src/index.ts:323-327`（压缩流程内先剪枝 → 重测 → 达标即免摘要） |

### G 替换式检查点 + 事务 id

| 证据 |
| --- |
| `packages/compaction/compaction-basic/src/region.ts:491,506-507`（替换式 `user/message`；`CHECKPOINT_PREAMBLE` + `<compacted-summary>` 框架） |
| `packages/compaction/compaction/src/checkpoint.ts:22,30,45,53`（`COMPACT_CHECKPOINT_MARKER`、`compactCheckpointSource`、`isCompactCheckpointSource`；无框架依赖的叶子出口） |
| `packages/compaction/compaction/src/brand.ts:4,11`（`Branded<'CompactionId'>` + 身份函数） |
| `packages/compaction/compaction/src/types.ts:41`（`CompactionResult.shadowedRange` = **位置跨度**，不是数值区间；替换会让 seq 非单调） |

### H 五条退化防护

| 证据 |
| --- |
| `packages/compaction/compaction-basic/src/region.ts:107-141`（尾部按已计价 token 从尾向前累加出 `keepFromIdx` + 平衡回退）、`:349-355`（start/end 均须平衡边界）、`:368`（保留预算计算）、`:418`（净收缩门） |
| `packages/compaction/compaction/src/tool-pairing.ts:1`（`toolPairingBalancedBefore/After`） |
| `packages/compaction/compaction-basic/src/summarizer.ts:33,66-71,203,217`（检查点固定 8 段模板 + "合并而非照抄前一检查点"规则） |
| `packages/compaction/compaction-basic/README.zh.md:82`（"无安全范围时不写任何内容"） |

### I 溢出恢复的进展证明

| 证据 |
| --- |
| `packages/compaction/compaction-basic/src/index.ts:190-234`（`agent/request-error`：仅 `CONTEXT_WINDOW_EXCEEDED` 才接管；重试前快照 `surface.replaceGeneration`，**严格前进**才返回 retry；`maxOverflowRetries` 上限） |
| `.agents/notes/archived/architecture/2026-07-10-after-call-compaction-pressure-and-overflow-recovery.zh.md:65`（"返回结果但没改模型可见表层"不足以授权重试） |
| `packages/compaction/compaction-basic/src/index.ts:158`（`agent/pre-step` 压力监听器**永不 reject**：失败只记警告并 `next()`，绝不让压缩失败阻断轮次） |

### J 锁的崩溃可检测性

| 证据 |
| --- |
| `packages/compaction/compaction-basic/src/region.ts:210,237,245`（先落 `compaction/start` 再异步；顺序固定）、`:432,452`（`assertStable`：自动=整表层深比较，手动=仅所选区间）、`:580`（孤儿锁判定：早于最新生命周期边界的未匹配 start 视为陈旧） |
| `packages/compaction/compaction/src/index.ts:157`（恰好一次 `compaction/end`） |
| `packages/compaction/compaction/README.zh.md:126`（"标记是时间点，而不是排他的容器"） |
| `.agents/notes/archived/feature/2026-07-30-queued-manual-compaction.zh.md:41,47`（空闲期维护；排队输入保持 FIFO） |

### K 占位符 + 复用已有工具取回

| 证据 |
| --- |
| `packages/spill/spill-policy/src/index.ts:26-27,34,37-39`（`maxInlineTokens`；省略配置 = 禁用；非法值加载期抛错）、`:49-53,93-98`（类型白名单 = 全 text/image；`read` 工具豁免）、`:103-124`（head/gap/tail 预算切分 + 尾部通知）、`:133-139,150-155`（触发点与 PTC 日志副本路径） |
| `packages/spill/spill-policy/src/notice.ts:6-9,20-24,44-58`（通知格式：省略子句 + LOCATION + 引导分隔符 + `retrievalHint`；`hasSpillNotice` 只做文本识别，**不可**作为"确实外置过"的判据） |
| `packages/spill/spill-local/src/index.ts:152-161`（`retrievalHint` = "Use read with offset/limit, or grep this path to search within it."） |
| `packages/spill/spill/src/index.ts:20-28,43-49`（seam 只有一个 `saveText`；不定义保留策略与检索 API） |
| `packages/spill/spill-local/src/store.ts:16,36-39,52-74,78-81,109-116`（0700 根、`sha256(sessionId)[:12]` 目录、随机前缀 + 单射转义、`open(path,'wx',0o600)`） |
| `packages/context/session-reference/src/spill.ts:40-76`（transcript 每行 ≤64 码点，保证长单行可分页读取） |
| `packages/util/output-retention/src/index.ts:412-421,436-442`（`describeOmitted` 只对精确计数打印数字；`formatRetentionNotice` 把中性省略子句与**工具自有的**恢复指引拼一行） |

### L 降级永不污染调用语义

| 证据 |
| --- |
| `packages/spill/spill-policy/src/index.ts:111-131`（全包 try/catch；`saveText` 拒绝、图片不可读、计量失败 ⇒ 记警告并保留原内容；**不把成功的工具调用变成错误**） |
| `packages/context/session-reference/src/spill.ts:31-56`（`fullSnapshot` 区分 `save-failed` 与 `storage-not-configured`） |
| `packages/context/session-reference/src/index.ts:379-389`（固定部分装不下预算 ⇒ 整条准备失败，绝不发布半截上下文） |

### M 精确锚 + 有符号增量

| 证据 |
| --- |
| `packages/llm/token-meter/src/index.ts:146,158,164-171`（`measure`；`optionalHeaderEquals`；`usage >= 完整启发式锚点价` 才采信，否则回退估算）、`:306-318` |
| `packages/llm/token-meter/src/usage-projection.ts:158-215`（`pressureTokens` 只在样本处前进；`projectedTokens = max(0, pressure + surfaceTokens − sampledSurfaceTokens)`；采样点在事件加入表层**之前**打点） |
| `packages/llm/token-meter/src/projection.ts:20-48`（`contextPressure` / `contextBreakdown` 语义："Present these as approximations of composition, never as a total"） |
| `packages/llm/token-meter/src/estimate.ts:13,16,19,79,101`（`CHARS_PER_TOKEN = 4` + 块/角色结构开销；system 与 tools 的计价路径） |
| `docs/subsystems/token-meter.zh.md`（`baseline.kind` = usage/estimated/none；`surfaceDeltaTokens` 有符号） |
| `.agents/notes/archived/bug-fix/2026-08-05-context-meter-blind-to-compaction.zh.md`（"压缩后仪表不动"的真实故障与修法） |

### N 影子价协议

| 证据 |
| --- |
| `packages/compaction/compaction/src/types.ts:74`（影子价协议：replace 的价格由紧邻其前的计量事件声明，且必须用固定启发式，否则纯 fold 的累计值漂移） |
| `packages/compaction/compaction-basic/src/region.ts:380-381`（`shadowedTokenCount` = Σ 启发式价 vs `shadowedRouteTokenCount` = Σ 路由价，双价显式分离） |
| `packages/compaction/compaction-tool-result-pruner/src/index.ts:160`（`compaction/prune` 紧邻替换事件） |
| `packages/llm/token-meter/src/surface-fold.ts:107,136`（`planSurfaceTokens` 只读可失败 / `commitSurfaceTokens` 原地不可失败） |
| `.agents/notes/archived/bug-fix/2026-08-24-token-meter-surface-fold-plan-commit.zh.md`（"复制换原子性"造成 O(S²)；一致性靠执行顺序而非拷贝） |
| `.agents/notes/archived/bug-fix/2026-08-06-token-surface-unpriced-replace-compatibility.zh.md`（未计价替换中性折叠；矛盾声明仍抛错） |

### O 一次基线 + 只发差量；只缓存元数据

| 证据 |
| --- |
| `packages/context/agent-instructions/src/state.ts:27-40,44-58`（`InstructionVersionState`；注释 "instruction prose is deliberately not retained"）、`:140-158,372-420`（元数据未变即跳过） |
| `packages/context/agent-instructions/src/digest.ts:11-13,22-24`（原样 SHA-1 用于变更检测 / trimmed SHA-1 用于同候选组去重） |
| `packages/context/agent-instructions/src/index.ts:141-175,238-283,246-266,334-360,362-379`（首次基线 + delta；无 watcher，靠成功的 fs 调用触发对账；无 `ctx.fs` 时静默 no-op） |
| `packages/context/agent-instructions/src/config.ts:11-14,37-43,66-84`（`maxBytes` 必填、无默认） |
| `packages/context/agent-instructions/src/render.ts:16-25,87-93,375-399`（`<system-reminder>` 围栏 + 正文内结束标签转义；渲染为空时不提交版本状态） |
| `packages/context/file-reference-local/src/search.ts:84-170`（每 agent 一个搜索器 + 单调失效计数 + 后台重建，陈旧索引继续作答） |
| `packages/context/time-context/src/index.ts:185-222`（`refreshIntervalMs` 默认 600000；抑制判据来自 fold 出的持久 `lastInjectionTime`） |
| `packages/context/tmux-context/src/index.ts:238-270`（渲染状态与上次注入**逐字符相等**即完全不注入；仅 step 1；前置插入） |

### P 指纹覆盖结构化事实

| 证据 |
| --- |
| `.agents/notes/implemented/feature/2026-08-05-context-form-vocabulary.zh.md`（目录身份从"渲染后文本"迁到**持久条目**；`escapeText` 确定且单射故 digest 等价） |
| `packages/skill/tool-skill/src/index.ts:229-244`（`digest` 与 `history.visibleDigest` 双比较，不变则连临时目录消息也过滤）、`:330-337`（`digestCatalogEntries`：每项 `JSON.stringify([name, description])` + 换行 + sha256） |

### Q 块级决策可观测

| 证据 |
| --- |
| `packages/core/agent-loop/src/agent.ts:631-663`（`request/header` 写入条件与 `reason`）、`:668-683`（`request/context` 仅在路由字段变化时写）、`:655-663`（`developer/message` 只带工具增删差量） |
| `docs/subsystems/session.zh.md:181-210`（`request/header` = 信封；`request/context` = 路由元数据，刻意不进 header，因为 header 是逐字段比较的重建约定） |
| `docs/architecture.zh.md`（轮次流程：`step/start` → `agent/request` → 提交 `system/message` → …；三个事件域：会话事件 / agent 事件 / 能力事件） |

### R 失败姿态纪律

| 证据 |
| --- |
| `packages/llm/token-meter/src/turn-usage.ts:126-147,178`（精确逐轮用量 fail-closed：任一缺失/矛盾即整份不可用） |
| `.agents/notes/archived/bug-fix/2026-08-06-token-surface-unpriced-replace-compatibility.zh.md`（历史宽容、现行生产者严格） |

---

## 3. 我方现状证据（openLuo 侧，用于主报告的"我方现状"栏）

| 事实 | 证据 |
| --- | --- |
| 块序 = Region 枚举序分组 + 区域内 Priority 降序；`TimeContext` 排第 2 位 | `openLuo.AgentContext/Core/Models/ContextModels.cs:13-33` |
| 装配器注释声称"呈现顺序…保证系统块前缀稳定（利于模型侧前缀缓存）" | `openLuo.AgentContext/Infrastructure/DefaultContextAssembler.cs:14-18` |
| 确定性决胜键（呈现序与裁剪序都用 `Id` 序号序） | `openLuo.AgentContext/Infrastructure/DefaultContextAssembler.cs:173-175,192-196` |
| 裁剪 = 保底全收 + 可裁区域 Priority 降序贪心（**可跳过中间条目**） | `openLuo.AgentContext/Infrastructure/DefaultContextAssembler.cs:151`、`openLuo.AgentContext/Core/ContextRegionPolicies.cs` |
| 历史按条数保留（`ConversationRetainCount` 默认 24） | `openLuo.AgentContext/Infrastructure/DefaultAgentContextSession.cs:95`、`openLuo.Application/Composition/ComposedAgentRuntime.cs:278`、`openLuo.Application/Composition/ConfigContextLimitsProvider.cs:23` |
| 计费器（含 CJK 权重）与预算快照 | `openLuo.AgentContext/Core/Models/ContextBudgetSnapshot.cs`、`ITokenCounter` 实现，`openLuo.Server/TurnBudgetPolicy.cs` |
| 压缩/摘要/外置**零命中** | `grep -rl 'Compact\|Summariz\|Spill' --include=*.cs openLuo openLuo.Application openLuo.AgentContext openLuo.Capabilities openLuo.Server` ⇒ 无输出 |

---

## 4. 文档 ↔ 代码漂移（引用时以代码为准）

| # | 文档声称 | 代码实际 | 证据 |
| --- | --- | --- | --- |
| 1 | 摘要调用 `maxTokens: 8192` | 默认 = `headroomTokens` = **65536** | `packages/compaction/compaction-basic/src/config.ts:76` vs `.agents/notes/archived/architecture/2026-07-10-after-call-compaction-pressure-and-overflow-recovery.zh.md:53` |
| 2 | `contextPressure.stateVersion` "提升到 3" | 实际 **5** | `packages/llm/token-meter/src/usage-projection.ts:175` vs `.agents/notes/archived/bug-fix/2026-08-05-context-meter-blind-to-compaction.zh.md:19` |
| 3 | 压缩前缀"从 `requestHeader()` 取 system 与 tools" | system 前缀取自 **surface 节点 0 的派生消息**，`requestHeader()` 只提供 tools | `packages/compaction/compaction-basic/src/region.ts:556` vs `.agents/notes/archived/bug-fix/2026-07-21-compaction-summary-prefix-cache-reuse.zh.md:19` |
| 4 | 外置配置键写作 `maxInlineBytes` | 实际键名 `maxInlineTokens` | `packages/spill/spill-policy/src/index.ts:26-34` vs `.agents/notes/archived/feature/2026-07-26-ptc-dispatch-log-spill.zh.md:19`、`.agents/notes/archived/feature/2026-07-30-search-render-card.zh.md:28` |
| 5 | `docs/subsystems/spill.zh.md` 未记录 `read` 豁免与 PTC 日志副本分支，也未提"无容量上限" | —— | `packages/spill/spill-policy/src/index.ts:133-155`、`packages/spill/spill-local/README.zh.md` |

---

## 5. 选题纠错（本次侦察推翻的两个前提）

| # | 原以为 | 实际 | 影响 |
| --- | --- | --- | --- |
| 1 | `2026-07-30-composer-context-stack-order` / `2026-08-02-todo-first-composer-context-order` 是**提示词块序**规则 | 讲的是 **Web 输入框上方卡片堆叠几何**（Todo 0 / Goal 10 / Queue 20、6px 间距、776px 包装层与负外边距重叠）；提示词顺序的真源是中心分配表 | 主报告 **B** 只借两条抽象规则（显式数值 order 为真源、消费层不得二次修正顺序；几何/分隔约定与顺序约定解耦），不搬其产品语义 |
| 2 | `2026-08-11-trajectory-conversation-context-assembly` 讲"轨迹 → 提示词"组装 | 讲的是**客户端 Chat/Trajectory 视图组装**（Definition/ViewBuilder、Location、增量重算）；真正的提示词组装 = `Session.deriveMessages()` 折叠 surface | 该笔记不作为证据；提示词组装证据改用 `packages/core/session/src/index.ts:984`、`packages/core/session/src/surface.ts:50-56,120-144` |

纠错 1 的旁证：`.agents/notes/archived/bug-fix/2026-07-30-composer-context-stack-order.zh.md`、`.../2026-08-02-todo-first-composer-context-order.zh.md`、`packages/client/ui-conversation/src/client/skeleton/TodoPanel.tsx:116-117`、`packages/client/ui-goal/src/client/index.ts:92-96`、`packages/client/ui-conversation/src/client/queue/QueueDock.tsx:392-397`。

---

## 6. 如何复核

```sh
# 0. 先确认快照未变（否则全部行号作废）
git -C /home/yoitsuholo/Code/deepseek-harness rev-parse HEAD    # 期望 d743267388641bc76f17c45ce8b4d231aed1d32c

# 1. B：中心分配表 + 决胜规则
sed -n '131,175p;238,246p;490,501p' /home/yoitsuholo/Code/deepseek-harness/packages/core/system-prompt/src/index.ts

# 2. A/C/D：提示词投影、首节点保护、空块占位
sed -n '65,103p;114,160p' /home/yoitsuholo/Code/deepseek-harness/packages/core/agent-loop/src/runtime-context.ts
sed -n '134,139p;489,506p' /home/yoitsuholo/Code/deepseek-harness/packages/core/session/src/surface.ts

# 3. E/F/H/I：压缩阈值、剪枝、选段与溢出恢复
sed -n '20,23p;75,79p;191,198p' /home/yoitsuholo/Code/deepseek-harness/packages/compaction/compaction-basic/src/config.ts
sed -n '107,155p;349,355p;418p' /home/yoitsuholo/Code/deepseek-harness/packages/compaction/compaction-basic/src/region.ts

# 4. K/L：外置策略与降级姿态
sed -n '26,60p;93,131p' /home/yoitsuholo/Code/deepseek-harness/packages/spill/spill-policy/src/index.ts
sed -n '1,60p' /home/yoitsuholo/Code/deepseek-harness/packages/spill/spill-policy/src/notice.ts

# 5. M/N：锚点、增量与影子价
sed -n '146,175p' /home/yoitsuholo/Code/deepseek-harness/packages/llm/token-meter/src/index.ts
sed -n '158,215p' /home/yoitsuholo/Code/deepseek-harness/packages/llm/token-meter/src/usage-projection.ts
```

---

## 7. 适用性前提对照（为什么本目录的结论要按前提打折）

### 7.1 对方的前提：单一全局上下文

| 前提 | 证据 |
| --- | --- |
| 一个运行实例 = 按 profile 叠加出的一棵插件树，**一份统一配置与会话世界** | `docs/architecture.zh.md`（"运行中的 `dsh` 是一棵插件树，由启动时按序叠加的各层组合而成"） |
| 一个会话 = **一条 append-only 日志 + 一个 surface**；`deriveMessages()` 是唯一投影 | `docs/subsystems/session.zh.md`（事件词汇 / surface 类型 / 派生历史） |
| 作用域是**扁平、按 agent** 的注册归属（最近层遮蔽）；它解决的是"同一进程内多个 agent 各自看到什么" | `docs/subsystems/scope.zh.md`、`.agents/notes/archived/architecture/2026-07-08-agent-scope-contexts.zh.md`（"作用域是扁平的；解析永不遍历父级或兄弟"） |
| 因此"易变内容追加进**同一条**历史"是安全的，全局前缀 / 全局块缓存成立 | `packages/context/time-context/README.zh.md`、`packages/spill/spill-policy/README.zh.md`（"仅追加；位于可复用请求前缀之后"） |

### 7.2 我方的前提：容器化 + 动态构建 + 方向性可见性

| 前提 | 证据（本仓库） |
| --- | --- |
| 角色拥有自己的私密观感，**不能因为群聊而泄漏** | [`../../architecture/context-spaces.md`](../../architecture/context-spaces.md) R3 |
| 空间之间是**方向性**的：个人空间可**全量读取**"包含该用户的群空间"，群空间只能得到个人空间中被**发布**的部分 | 同上 R5、§2.4（关系类 × 制品类的方向性） |
| **可读 ≠ 注入** | 同上 §2.6 |
| `ContextScope{Global,Session,Thread,User}` 是**缓存域**而不是访问域；缺的是**装配期的可见性检查点** | 同上 X1 |
| 今天只有"全局可见/不可见"两档且方向是错的（`user_id = ''` 对所有空间可见） | 同上 X3 |
| 一次回合 = 一个 `SessionId`，装配器整回合只跑一次；跨空间是特例 | 同上 X6 |
| 多群历史会把预算打爆，且模型会把私聊/群聊的话混着说——这是**正确性**问题 | 同上 §2.6 与需求节（"混着说"） |

### 7.3 由此得到的迁移规则

| 规则 | 说明 |
| --- | --- |
| **不搬结构** | `surface` 的 replace/遮蔽、每会话单一 surface、提示词作为"第 0 号节点"、`in-history` 提示词追加——都以"单一日志"为前提 |
| **搬纪律** | 排序确定性（B）、结构不可裁在裁剪点强制（C）、空块占位（D）、稳定内容在前 / 易变内容在后（A 的排序部分）、指纹按结构化事实（P）、块级决策可观测（Q）、失败姿态（R） |
| **重新表述** | A 的"只追加、未变零写入"与 O 的"一次基线 + 只发差量"要落到**某个空间自身的持久流/容器**上（"该空间"才是一条可追加的流），而不是全局唯一的一条流 |
| **优先级** | 削减类（E–J、K、M–N）**暂缓**至空间与装配模型定案；本目录只作取材库（见主报告文首声明与 §6） |
