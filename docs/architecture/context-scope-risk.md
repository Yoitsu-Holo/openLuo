# 上下文系统：全局 / 局部改造风险识别

> **状态**：风险识别（本轮**不实施**，按用户要求先识别）。
> **结论**：当前上下文装配是「**单会话扁平管线**」，未区分 **全局**（所有局部推理共用的身份 / 规则 / 时间）
> 与 **局部**（好友 / 群聊 / 线程）。迁移到「全局 + 局部」是**结构性改动**，牵动内核契约、缓存、
> 记忆键控与协议层；下面先固化风险与建议，避免后续边改边乱。

---

## 1. 现状（证据）

| 维度 | 现状 | 证据 |
| --- | --- | --- |
| 装配 | 每回合 `IContextAssembler.BuildAsync(ContextBuildRequest)`；贡献者逐块产出 | `openLuo.AgentContext/Core/IContextAssembler.cs`、`Core/IContextContributor.cs` |
| 贡献者 | companion(Identity,100) / party(Identity,50) / world(SceneState,60) / memory(LongTermMemory,40) / TimeContext / Platform / Capability | 各 `extensions/*/XxxExtension.cs`；`openLuo/Composition/*Contributor.cs` |
| 会话 | `DefaultAgentContextSession` per-session 持有 `_current` 快照（贡献 + 对话），`CreateTurnSnapshotAsync` 全量重建 | `openLuo.AgentContext/Infrastructure/DefaultAgentContextSession.cs` |
| 键控 | 记忆按 `(SessionId, SubjectId)`；`TurnRequest.ActorId` 不落库 → 群内无用户维度 | `extensions/memory/MemoryExtension.cs`；`openLuo.Capabilities/Core/IAgentRuntime.cs` |
| 会话键 | QQ 会话 = `qq-{scene}-{targetId}`（一个群 = 一个会话 = 一份历史 / 记忆） | `openLuo.Qqbot/Interfaces/QqRuntimeBridge.cs` |

**要害**：上下文只有「一个会话」这一层隔离轴，没有「全局共享」与「域内独立」的区分。

## 2. 目标形态（用户描述）

- **全局**：所有局部推理都需要 —— 身份 / persona、时间、运行时规则、长期人格设定、全局世界状态。
- **局部**：每个「对话域」独立 —— 好友、群聊、线程；各自的对话历史 / 记忆 / 在场状态。

## 3. 风险清单

| # | 风险 | 说明 |
| --- | --- | --- |
| **R1** | 契约无 scope | `ContextBuildRequest` / `IContextContributor` 无 scope 概念，无法表达「该贡献属全局还是局部」。需给贡献打 `scope` 并分层合并。 |
| **R2** | 全局内容重复 | per-session 快照各自持有全局块（身份 / persona / 时间）→ N 会话放大 N 份；全局变更（改 persona）需失效所有会话快照。 |
| **R3** | 键控维度不足 | 记忆 / 历史按 `(SessionId, SubjectId)` → 群聊下「用户×角色」不可表达；引入 `threadId` 后再加一维。需统一「上下文域键」。 |
| **R4** | 会话边界单一 | `ConversationId ≡ SessionId`，无「会话内会话」轴；好友 / 群 / 线程需要不同隔离级别（共享历史 vs 独立历史）。 |
| **R5** | 缓存粒度 | 全局贡献里「稳定」（persona）与「易变」（时间）混在一起，每回合全量重建；稳定部分应缓存，局部每回合组装。 |
| **R6** | 并发 | 本轮已加 per-session 回合闸；全局快照共享后，全局失效 / 重建需与局部回合协调（读多写少 → 版本化不可变快照）。 |
| **R7** | 协议映射 | wire 侧已有 `threadId` / `userId`（本轮补），但内核与会话模型未落地 → 需与「上下文域键」对齐。 |
| **R8** | 装配方向 | `IContextAssembler` 在内核（`openLuo.AgentContext`）；全局域可能需跨会话聚合（如「我的所有群」）→ 需明确是否引入全局层服务。 |

## 4. 建议（后续，非本轮）

1. 引入 `ContextScope { Global, Session, Thread, User }` + `IContextContributor.Scope`（先加字段，默认 `Session`，向后兼容）。
2. 全局贡献缓存为**不可变快照**（带版本）；会话快照 = 全局（**引用**）+ 局部（组装）。
3. 统一「上下文域键」`{scope, key}`；记忆 / 历史 / 在场按该键存取。
4. 会话模型升级：`sessionKey`（平台目标）+ `threadId`（会话内线程）+ `userId`（说话人）。
5. 迁移顺序：先引入 scope → 缓存全局 → 键控扩维 → 协议落地。

## 5. 与本次内核改动的交互

- 本轮已加 **per-session 回合闸**（`ComposedAgentRuntime._turnGates`）：这是上下文并发安全的基础；
  全局化后需扩展为「全局读快照 + 会话写」的并发模型（R6）。
- 本轮 **输出队列解耦**（`InMemoryOutputQueue` 每会话日志 + `sequence` + `ReadSince`）已按「会话」分区，
  天然贴合「局部」边界；全局事件（如全局提醒）可走空会话键（`conversationId = null`）。
