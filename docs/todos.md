# 待办项与缺陷项（todos / tds）

> **状态**：**登记表（活文档）**。本文登记**缺陷项**与**待办项**，不含实现、不含设计正文。
> **分工**：机制与设计的唯一来源在各设计文档（[`context-spaces.md`](architecture/context-spaces.md)、
> [`context-domains.md`](architecture/context-domains.md)、[`context-budget.md`](architecture/context-budget.md)、
> [`skills-workflows.md`](architecture/skills-workflows.md)、[`hub-protocol.md`](architecture/hub-protocol.md)、
> [`log-store.md`](architecture/log-store.md)）；技术债的**定期检视**结论在 [`layers.md`](architecture/layers.md)。
> 本文只做**索引 + 状态 + 判据**，不复制它们的内容——一条待办若已有设计文档，这里只留一行指针。
> **登记规则**：每条必须有 ①**可核实的证据**（代码路径 + 类型/成员名，或一条可复现判据）②**关闭判据**（怎么算做完了）
> ③**来源**（在哪次工作中发现、关联哪份文档）。关闭后**删行**（历史留在 git 与提交信息里），不保留"已解决"区。

---

## 1. 缺陷项（defects）

> 判据：**已核实**的现状与契约/意图不一致，且**消费方可见**（客户端、模型、扩展作者或后来人会因此做错事）。

| # | 缺陷 | 证据（已核实） | 影响 | 修法 | 关闭判据 |
| --- | --- | --- | --- | --- | --- |
| **D1** | **技能/工作流能力未绑定 invoker**：`core:load_skill` / `core:unload_skill` / `core:run_workflow` 在生产路径落到兜底 invoker，调用即失败 | `ServiceCollectionExtensions`（`ICapabilityDispatcher` 工厂）组装 `canonicalInvokers` 时 `switch` **只处理 `McpCapabilitySource` 与 `A2ACapabilitySource`**（其余 `_ => null`）；`SkillCapabilitySource` / `UnloadSkillCapabilitySource` / `WorkflowCapabilitySource` 的 `CreateInvoker()` **从未被调用**。`DefaultCapabilityDispatcher` 解析顺序 canonical → kind → default，`kindInvokers` 未传，默认 invoker 正是 `UnboundCapabilityInvoker`（返回 `Failed` + `capability '<id>' is not bound to an invoker`） | 模型在 `[Skills]` 目录里被引导调用一个**必然失败**的工具；`core:load_skill` 与 `core:run_workflow` 端到端不可用（`data/skills/*.md` 的装载与目录块本身正常） | 把三个源的 invoker 纳入同一处绑定（更彻底：让 `ICapabilitySource` 统一暴露 `CreateInvoker()`，避免"新增源忘记加 case"重犯） | 一条**经 hub** 的 E2E 调 `core:load_skill` 拿到正文（不是直接 `new …Source().CreateInvoker()`） |
| **D2** | **wire 契约注释过期**：`TurnRequestDto` 声称 `Meta/UserId/Mentions/ThreadId` **仍未实现** | `openLuo.Protocol/Models.cs` 的 `<remarks>` 原文："**仍未实现**的字段（`Meta/UserId/Mentions/ThreadId/IdempotencyKey`）只被反序列化、不产生任何效果，客户端不得依赖其语义" | `UserId`/`ThreadId`/`Mentions` **已落地**（`HubServer` 映射进内核 `TurnRequest`，桌面三端已在发送）；**协议注释是唯一跨边界契约**，过期即误导扩展/客户端作者 | 按实际消费情况重写 remarks（哪些已消费、哪些仍未实现；`Meta` 确实仍无人发送，见 T1） | 注释与实际映射逐字段一致，并由测试或文档核对留痕 |
| **D3** | **调度器注释过期**：`InMemorySchedulerService` 声称"周期（Cron）暂不支持" | 类注释原文："周期（`Cron`）暂不支持（`Add` 抛 `InvalidOperationException`，Hub 回 `1202`）"；而同文件的 `Add`/`Next` 路径已 `CronSchedule.TryParse` 并计算 `Next(now)` | 注释把"不支持"写成对外契约，会误导调用方放弃 cron | 改注释为实况（cron 已支持；若某些边界确实不支持，写清是哪些） | 注释与 `CronSchedule` 实际行为一致 |
| **D4** | **`sessions` 缺唯一约束**：`(subject_id, agent_id, conversation_id)` 无唯一约束 | `HubStore` 建表只有 `session_id TEXT PRIMARY KEY`；内存反查表 `SessionStore` 由该表重建（`FindByConversation` 依赖三元组） | 多端/多进程同时打开**同一段对话**时可能各建一个会话 ⇒ **历史与记忆被劈成两半**，事后不可合并（空间模型下这是常态而非边缘） | 补唯一索引（或在寻址层改为按三元组 upsert） | 并发打开同一 `(subject, agent, conversation)` 只产生一个会话（测试/冒烟可复现） |
| **D5** | **提示词块序把易变块放在前缀** ⇒ provider 侧 KV cache 结构上不可能命中 | 块序 = 先按 `ContextRegion` 枚举序分组、**区域内按 `Priority` 降序**（`ContextModels` 注明"渲染顺序 = 本枚举顺序"），而 `TimeContext` 排第 2 位；时间块每回合都变（`host.time` 贡献者刻意不设 `CacheTtl`，注释写明"给 TTL 等于把时间冻结"）；`[Capabilities]` 块被单独剥出、**工具目录块插在全部 SystemBlocks 之后** | 每回合前缀位移 ⇒ 缓存收益恒为零（成本/延迟白付）；且**空块跳过**与**按相关度分数排序**会进一步放大位移 | 块序重排为"稳定前缀 → 易变后缀"，空块固定占位、分数排序块置尾、裁剪只动后缀（`runtime-modules.md` §2.5 / M8） | 每回合 `[ctx]` 记录**前缀指纹**：连续同类回合（同一空间、无状态变化）指纹应稳定（M9） |
| **D6** | **跨角色委派：契约称 30s 超时，实现忽略该参数；且无递归深度限制** | `openLuo.Modules.Agent/Application/Runtime/AgentRuntimeHub.cs` 有 `DefaultRequestTimeout = 30s` 并在注释里称"调用方不传 timeout 时用它"，但 `RequestAsync` **不使用该参数**；`extensions/party` 的 `AskCharacterInvoker` 同步 `await` 它，而它内部对 `party-{gameId}-{character}` 会话跑**完整内核回合**；全仓无递归/深度限制 | 委派可能**永不返回**（外层回合被挂住）；`A 问 B 问 C` 无上界；且注释/常量与行为不符（下一个人会按注释设计超时策略） | 真正使用超时 + 递归深度上限 + 委派继承**受限视角**（`context-spaces.md` §5.2/§5.6、`workers-and-hooks.md` W4） | 委派有可观测的真实超时；超过深度上限时**拒绝并留轨迹**（而不是继续嵌套） |

## 2. 待办项（未接线 / 未实现）

> 判据：机制或字段**已存在但无消费者**，或需求已明确但尚未实现。与缺陷项的区别是"没错，只是没做完"。

| # | 待办 | 现状证据 | 关闭判据 | 关联文档 |
| --- | --- | --- | --- | --- |
| **T1** | **`Meta` 无人发送** ⇒ `PlatformContextContributor` 恒空（角色**不知道自己处在群还是私聊**） | 贡献者读 `extras` 的 `scene` / `senderName` / `channelId` / `groupName`；客户端（CLI/TUI/GUI/QQ 桥）**零处**设置 `Meta`（桌面端只发 `OPENLUO_USER_ID` / `OPENLUO_THREAD_ID`） | 群里问"这是哪个群/我在哪"能答对；私聊与群聊的上下文块可区分 | [`context-spaces.md`](architecture/context-spaces.md) X10/M0 |
| **T2** | **群/成员身份只有 wire 空壳** | `Messages.Group.cs` 的 `MemberDto` / `MemberJoinedEvent` / `MemberLeftEvent`、`EventTypes.MemberJoined/Left`、`Features.Group` 均已登记进闭合集合，但**零生产者、无表、无端点**；`Features.Group` 未在 welcome/version 广播；`turn.request.channelId` 到达内核后**零消费者** | 成员表 + 名册贡献者 + 成员事件真实发射；`Features` 广播空间能力 | [`context-spaces.md`](architecture/context-spaces.md) X10/M0 |
| **T3** | **关系/信任无授权消费者，且状态存储分工未定** | 关系在 `Modules.WorldState`（game.db `state_values`，`char_status.relationship_stage`，由 affection 派生）**不参与授权**；`hub.db.subject_state` 的写入路径是活的（`DefaultCapabilityDispatcher` 提交 `MutationCollector` → `IStateTransaction.CommitAsync` → `IStatePersistence`），但**没有任何能力产生 `MutationIntent`**（`extensions/world` 只做 `ValidateAsync`）⇒ 实际为空 | 关系等级有**单一真值**并被授权层引用；两套存储的分工在文档里写死 | [`context-spaces.md`](architecture/context-spaces.md) X9/§4.4 |
| **T4** | **`CapabilityPermissions` 的 `AllowMutation` / `AllowExternal` / `AllowDelegation` 零消费者** | 三个属性默认 `true`，全仓只有定义处出现；`DefaultCapabilityCatalog` 只用 `AllowedCanonicalIds` / `AllowedKinds`；生产恒"空集=全放行" | 三者有真实判定点（或按"零消费者即删"原则删除，改由空间策略表承担） | [`context-spaces.md`](architecture/context-spaces.md) X4/§4.2 |
| **T5** | **零使用者机制：`presence.subscribe` 与 `targetClientId`** | `presence.subscribe` 只在 `ProtocolInfo` / `ProtocolRegistry` / 注释里出现，**客户端零处发送**；`HubBroadcaster` 的 `targetClientId` 定向投递**无生产者** | 要么客户端接上（多端在场/定向投递），要么删除（"机制必须有消费者"） | [`context-spaces.md`](architecture/context-spaces.md) §5.5 |
| **T6** | **作业系统空转**：零 `IJobHandler` 实现、调度↔作业无连线 | `InMemoryJobService` 有 hub.db 持久化与重启恢复，但**全仓零个 `IJobHandler` 实现**、`HubStartup` 未传 handlers（`POST /v1/jobs` 必然失败）；`InMemorySchedulerService` 与作业之间无任何连线 | 至少一个真实作业（摘要/记忆整理/回填）能按计划跑起来 | [`context-spaces.md`](architecture/context-spaces.md) X8/M3 |
| **T7** | **`Thread` 只记录不切分存储** | `conversation_turns.thread_id` 落库、`ContextDomainKeys.Thread` 键形状已有，但历史/记忆存储未按线程切分 | 线程语义定案（共享历史+标签，或独立历史）并落地 | [`context-domains.md`](architecture/context-domains.md) §7 |
| **T8** | **记忆压缩只剩恒真谓词** | `memories.is_compressed` 恒写 0、无写入者，而检索 SQL 里带 `AND is_compressed = 0`（恒真）；`GameConstants.MemoryCompressionCheckCount` / `MemoryCompressionLookbackDays` 零消费者 | 要么实现压缩（并让谓词有意义），要么删列与常量 | [`context-spaces.md`](architecture/context-spaces.md) X7/§3.3 |
| **T9** | **A2A 远程调用无会话/身份语义** | `A2ACapabilityInvoker` 是一次 `SendMessageAsync`，把 canonicalId + args + options 拧成纯文本，**无 contextId、无调用方身份**（只有配置里的 Tenant）；`CapabilityKind.RemoteAgent` 与 `SideEffectClass.Delegation` 已定义 | 委派能声明"以谁的视角、在哪个空间"发起，并有递归上限 | [`context-spaces.md`](architecture/context-spaces.md) §5.2/§5.6 |
| **T10** | **零消费者字段/机制清理候选（需逐条核实后删）** | 候选：`AgentDecisionContext.Budgets`（实际用的是 `DecisionLoopRequest.Budgets` 与 `CapabilityExecutionContext.Budgets`，需确认前者有无读者）、其余按"零引用核实 → 删除 → 重扫 → 构建兜底"流程逐条处理 | 每条候选都被证实零引用后删除（或补上消费者） | [`context-domains.md`](architecture/context-domains.md) §4（删除纪律） |
| **T11** | **Worker 声明与统一触发源不存在** | 唯一的"非对话"执行是 `HubServer.RunProactiveTurnAsync`：`SourceId="hub"` / `ActorId="system"` / `Text=""`，且**不读** `ScheduleDue.Kind/Payload`；`ScheduleInfo` 按 `sessionId` 寻址；全仓**无 worker 概念** | 轨迹能查到"这次 Run 属于哪个 worker、被什么触发"；主动回合能指定目标空间 | [`workers-and-hooks.md`](architecture/workers-and-hooks.md) §2.1/§2.3（W0） |
| **T12** | **Hook 机制不存在** | 全仓无 `IHook`/拦截器/事件总线（只有 `IMessageTagPipeline` 一处受限管线）；`DecisionEventKind{Decision,ToolCall,ToolResult,Output,Final}` 只用于流式输出，不作为扩展挂点 | 观察型 hook 覆盖全部生命周期点且不影响主流程；拦截型 hook 能拒绝一次工具调用并留原因；hook 报错不炸主流程 | [`workers-and-hooks.md`](architecture/workers-and-hooks.md) §2.5（W1） |
| **T13** | **定时两级与随机触发不存在** | 定时只有 `at`/`cron`（`ISchedulerService` ✓），无 tick/thinking 之分；无抖动/概率策略 | 定时 tick 在没有新素材时不产生 LLM 调用；随机触发可解释（记录判据/种子）并受空间静音与每日上限约束 | [`workers-and-hooks.md`](architecture/workers-and-hooks.md) §2.3/§3/§9 |
| **T14** | **UAC 模块不存在：权限判定散在边界层，内核无角色概念** | 判定分散在 `HubAuth`（`HubRoles`，secret→admin）、`HubEndpoints.System`（admin 前缀守卫）、`QqBotConfig.AdminUsers`（仅 `/command`）；`CapabilityPermissions` 是内核唯一钩子却**从不填充**；`HubConfirmationGate` 不看身份；关系状态在 game.db（`RelationshipStage` ← affection） | 权限能按「主体 × 空间 × 能力类」判定，且**策略只有一处真值**（UAC）；确认绑定 principal；关系与授予不被游戏数据重置清掉 | [`runtime-modules.md`](architecture/runtime-modules.md) §3/§4 |
| **T15** | **容器模型缺属性：无 schema 投影 / 写者白名单 / 新鲜度策略** | 唯一雏形是 `state_defs`（`value_type/retirement_policy/source_kind` 等，消费者 `WorldState/StateDefStore`）+ `state_values`；而 `memories` / `conversation_turns` 是**裸表**（无投影定义、无写者白名单、无新鲜度/过期策略）。非文本容器（米家设备、手环健康）无任何接入面 | 每类容器带 `kind/schema`、`direction`、`writers`、`freshness`、`projections`、`sensitivity`；时序容器只能注入**投影后的制品**（不是原始样本） | [`runtime-modules.md`](architecture/runtime-modules.md) §2.1（M7） |
| **T16** | **资源级权限矩阵不存在（无 `<容器>.<级别>` × role × `rwx`）** | 今天的授权只有**动作级**且从不填充：`CapabilityPermissions`（`AllowedCanonicalIds`/`AllowedKinds` 白名单，只被 `DefaultCapabilityCatalog` 消费）；**没有**资源级/级别级授权，也没有 role 解析（角色只存在于 HTTP 控制面）；`MemoryScope{CharacterPrivate,Shared}` 写入恒 0、不参与语义 | 权限能表达"某 role 对某容器某级别可 `r`/`w`/`x`"；未定义即 `---`；同格多条规则逐位 AND；派生制品继承源级别；`x` 与能力面求交集 | [`runtime-modules.md`](architecture/runtime-modules.md) §3.1（M10–M12） |
| **T17** | **"观测指标 / 回放前端"重复登记且无主** | `log-store.md` §9（「指标派生：由 `StatsAsync` 驱动错误率/回合曲线，接入 `/v1/metrics` 或前端面板」）与 `README.md` 未完成项「观测回放前端（轨迹已入库，回放可视化未做）」是同一件事，两边都不指向登记表 | 二者合并为**一条**（写清是"指标派生"还是"回放 UI"，或拆成两条），改指向本表 | [`log-store.md`](architecture/log-store.md) §9 与 `README.md` 路线图各加互指并指向本表 |

## 3. 待裁决（需先定案才能排期）

> 这些不是"做没做完"，而是**设计选择**。定案后转成待办项或删除。选项与推荐见 [`context-spaces.md`](architecture/context-spaces.md) §10。

**执行面（Worker/Hook）**：WQ1 hook 注册面、WQ2 hook 能否改 prompt、WQ3 worker 预算来源、WQ4 事件总线边界、
WQ5 worker 失败语义、WQ6 随机触发抖动策略——选项与推荐见 [`workers-and-hooks.md`](architecture/workers-and-hooks.md) §10。

**模块划分（UAC/Context 边界）**：M1 Context 的 Store/Assembler 是否拆程序集、M2 UAC 判定接口形态、
M3 关系是否完全迁出 game.db、M4 UAC 策略配置面、M5 UAC 存储归属、M6 空间解析归属——见
[`runtime-modules.md`](architecture/runtime-modules.md) §7。

| # | 待裁决 | 推荐 | 阻塞什么 |
| --- | --- | --- | --- |
| Q1 | 记忆真值键（`(agent, principal)` 全局 / 按空间 / 双层） | 双层（user 层跨空间可见 + space 层仅本空间可见） | M1 主键重构 |
| Q2 | 群空间能从个人空间拿到什么 | 只允许类型化事实（关系等级、公开经历、用户可审阅字段） | M3 发布管线 |
| Q3 | 权限升级批准人 | principal 本人 + admin（模型只提议） | M2 授权层 |
| Q4 | 关系状态真值归属 | `hub.db`（跨入口一致） | T3 / M2 |
| Q5 | 主动回合触发策略 | 按空间配置上限 + 用户可静音 | M4 |
| Q6 | `core:recall` 返回形态 | 返回带 `provenance` 的制品，不是拼好的文本 | M1 |
| Q7 | 跨空间发言（把私聊内容带到群里说） | 默认禁止，需发布规则显式放行 | M3 |
| Q8 | `system` 空间是否独立存在 | 是（作业/主动回合需要归属） | M4 |

## 4. 怎么用这份表

1. **加一条**：先拿证据（代码路径 + 类型/成员名，或复现判据），再填"关闭判据"；只有设计没实现 → 待办项；现状与契约不一致 → 缺陷项；需要人拍板 → 待裁决。
2. **做一条**：按"关闭判据"验证（单测/E2E/真机冒烟三者取适用的），通过后**删行**并写进提交信息。
3. **发现重复**：若同一问题已在设计文档里有条目，这里只保留**指针行**，不复制描述。
4. **定期检视**：技术债视角的总体检视结论仍写在 [`layers.md`](architecture/layers.md)，本文只保证"每条遗留都有主、有判据"。
