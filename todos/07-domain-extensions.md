# 07 · 领域扩展（第一版 5 个）

> 每个扩展 = 独立程序集 + extension.jsonc + 独立 data/。全部由 Extension Host 加载。
> 第一版不建 time 扩展（D47）：内核 IClock 默认 UTC。

## 1. memory 扩展（openLuo.Extension.Memory）

### 能力

```text
memory:search        深度检索（query/scope/limit）→ 记忆片段  [ReadOnly, Continue, Silent]
memory:write         写入记忆（content/importance/scope）   [Mutation, Continue, Silent]
```

### ContextContributor

```text
memory:baseline     每轮自动召回少量相关记忆（LongTermMemory region）[D32]
```

- 基线自动 + 深度按需（D32）
- 依赖宿主 IMemoryService（openLuo.Memory 模块能力经端口暴露）
- 失败 → Unavailable 结构化状态（D42）

### 数据

```text
data/memory-rules.jsonc   召回规则/降级策略（可选）
```

## 2. companion 扩展（openLuo.Extension.Companion）

> 拥有"人格身份"概念（D45）：PersonaId、角色画像、人格上下文。

### 能力

```text
companion:chat          显式聊天/回复生成（LLM 型，Terminal/MayComplete, Replyable）
companion:character_response  内部回复生成（供其他能力复用，不直接暴露）
companion:plan          可选规划能力（LLM 型，Continue, Silent）[不默认必经]
companion:state_propose_update  状态变更提案（转 world 的 mutation handler）
```

### ContextContributor

```text
companion:identity      角色人格（Identity region）
companion:profile       角色画像细节
companion:rules         行为规则（RuntimeRules region，按场景）
```

### Skills

```text
companion:gift-giving   赠礼行为指导（摘要注入 + 完整内容按需加载）
companion:school-day    校园日常行为指导
```

### 依赖

- `memory`（聊天需要记忆上下文）
- 宿主 `Llm`（LLM 型能力，D 边界：扩展可依赖 Llm）
- 宿主 `IConversationStore`（读历史）

### 数据

```text
data/archetypes/*.jsonc     角色原型
data/skills/*.jsonc         技能文档（摘要 + 完整内容）
data/characters/*.jsonc     内置角色清单（PersonaId/画像）
```

## 3. world 扩展（openLuo.Extension.World）

> 游戏状态领域。不感知"角色"语义，按 SubjectId 存状态（D45）。

### 能力

```text
world:state.read           读取状态（ReadOnly, Continue, Silent）
world:state.propose_update 提出状态变更（Mutation, Continue, Silent）
world:inventory.list       背包列表
world:inventory.read       单物品查询
world:shop.list            商店分类/物品列表
world:offer_gift           赠礼（Mutation, MayComplete, Replyable）
world:schedule.status      当前时段状态（上课/午餐等）
```

### ContextContributor

```text
world:state                当前状态摘要（SceneState region）
world:schedule             当前时段/规则（RuntimeRules region）
world:shop                 商店摘要（可选，按场景）
```

### StateMutationHandler

- 校验 mutable/derived/clamp/maxDelta（D20，字段规则在扩展侧）
- 状态键：`world:state:<subjectId>:<field>`
- 冲突检测经内核 IStateTransaction（D9）

### 依赖

- 无 time 扩展（D46）；时段窗口基于内核 IClock 计算 UTC 时间（虚拟时钟后续引入）
- 宿主 `IStateTransaction`、`IClock`

### 数据

```text
data/item-packs/*.jsonc     物品 + 分类（名称可中文，内核透传）
data/schedules/*.jsonc      时段窗口（class/lunch）
data/state-defs/*.jsonc     状态定义（mutable/clamp/maxDelta）
```

## 4. party 扩展（openLuo.Extension.Party）

> 多角色协作（D45：依赖 companion 的人格目录）。

### 能力

```text
party:list_characters      列出可联系角色（ReadOnly, Continue, Silent）
party:switch_character     切换当前角色（Mutation, Terminal, Silent）
party:ask_character        询问另一角色（Delegation, Continue, Silent/可配置）
party:chat_session         多角色会话（Delegation, MayComplete, Replyable）
party:task_assign          分发任务（Delegation, Continue, Silent）
```

### ContextContributor

```text
party:roster               当前可见角色摘要（Identity/WorldContext region）
```

### 依赖

- `companion`（人格目录：PersonaId/画像）
- 宿主 `IAgentRuntime`（委托调用另一会话，经内核隔离）

### 数据

```text
data/characters/*.jsonc     角色清单补充（可选，或并入 companion）
```

## 5. media 扩展（openLuo.Extension.Media）

### 能力

```text
media:fetch_random_image   随机图片（External, MayComplete, Replyable）[媒体输出]
```

### ContextContributor

```text
无（或按需贡献媒体源状态）
```

### 依赖

- 无（仅宿主 HttpClient/AssetStore 端口）

### 数据

```text
data/sources.jsonc          图片源配置
```

## 6. 跨扩展协作示例

```text
玩家说："送个礼物给她"

companion 视角：
  skill:gift-giving 摘要 → Agent 加载完整指导（core:load_skill）
  → world:inventory.list（检查背包）
  → world:offer_gift（赠礼，MutationIntent）
  → world:state.propose_update（关系变更）
  → companion:chat（生成回复）
  → 最终文本回复
```

```text
玩家说："问问小艾今天去哪"

party 视角：
  party:ask_character(target="小艾", question=...)
  → 内核 delegate 到另一会话（经 IAgentRuntime）
  → 结果回填
  → companion:chat 生成回复
```

## 7. 扩展测试契约（D38 配套）

每个扩展在 `openLuo.Extensions.Tests` 中覆盖：

- 能力注册（canonical id 正确、schema 完整）
- 能力行为（正常/失败/幂等）
- ContextContributor 贡献内容
- StateMutationHandler 校验规则
- 标签渲染器
- 依赖解析（缺失依赖 → 禁用该扩展）
