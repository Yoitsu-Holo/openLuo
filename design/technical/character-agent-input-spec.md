# CharacterAgent 输入范式

## 1. 目标

将 CharacterAgent 的入模输入统一为原生 chat 消息序列，彻底替代“将全部上下文压成单条 JSON user message”的旧方案。

统一输入顺序固定为：

1. `SystemMessage`
2. `EnhanceMessage[]`
3. `ChatMessage[]`

运行时推荐链路固定为：

1. 语义化 Agent Context
2. `AgentPromptInput`
3. 原生 chat 消息数组

本设计只描述 CharacterAgent 主推理链的输入规范，不描述状态评估器、记忆摘要器等其他独立 LLM 调用链。

## 2. 设计原则

- `SystemMessage` 只承载最高优先级、跨角色稳定的全局规则。
- `EnhanceMessage` 只承载“非对话历史”的结构化上下文。
- `ChatMessage` 只承载真实短期对话与必要的工具回写。
- 不允许把规则、资源快照、长期记忆伪装成普通聊天历史。
- 不允许再把角色上下文、工具目录、记忆、资源状态重新压回一个大 JSON 字符串。
- 上下文应按“规则 -> 增强块 -> 短期历史”的优先级顺序传入。
- 不应再让主运行时上下文直接退化成 prompt 成品袋子。

## 2.1 推荐运行时分层

- `AgentContextBase`：公共轮次上下文字段
- `CharacterAgentContext`：角色语义上下文
- `CharacterTurnRequest`：角色回合输入
- `Executor` input DTO：各阶段固定结构化输入

推荐调用链：

`CharacterTurnRequest -> CharacterTurnCoordinator -> Character*Stage -> Executor -> ILlmClient`

## 3. 消息类型职责

### 3.1 `SystemMessage`

职责：

- 通用角色 Agent 行为规则
- 工具调用协议
- 输出边界
- 不可违背的宿主约束

限制：

- 只允许一条
- 不放角色专属长设定
- 不放频繁变化的状态
- 不放长期记忆明细

### 3.2 `EnhanceMessage`

职责：

- 承载结构化增强上下文
- 使用 `[RULE] ... [/RULE]` 包裹内容块
- 让模型明确区分“上下文块”和“真实聊天历史”

限制：

- 每条只表达一个清晰主题
- 允许按需裁剪、增删、重排
- 不承担真实对话语义
- 不应成为无限堆料容器

### 3.3 `ChatMessage`

职责：

- 承载真实短期对话
- 保留最近几轮 `user / assistant / tool`
- 体现自然聊天时序

限制：

- 只能表达短期历史
- 不要混入资源快照、系统规则、长期记忆
- 顺序必须严格按时间排列

## 4. 标准块类型

CharacterAgent 侧推荐先收敛为以下固定 `EnhanceMessage.Rule` 集合。

### 4.1 `CHARACTER_PROFILE`

内容：

- 角色核心设定
- 人格特征
- 背景补充
- 说话风格
- 关系推进特征

来源：

- `openLuo/data/archetypes/*.jsonc`
- 角色背景模型解析与裁剪结果

### 4.2 `RUNTIME_CONTEXT`

内容：

- 当前场景
- 当前轮次说明
- 当前事件状态
- 宿主明确需要角色知道的运行时上下文

来源：

- 当前会话状态
- 当前轮调度上下文
- Host 侧动态注入结果

### 4.3 `RESOURCE_CONTEXT`

内容：

- 与当前轮决策直接相关的资源/状态
- 例如 `affection`、`trust`、`mood`、`relationship_stage`、时间、天气

限制：

- 只放相关项
- 不放无关全量状态表
- 不把整个 registry 或 snapshot 原样塞给模型

### 4.4 `LONG_TERM_MEMORY`

内容：

- 经召回、筛选、去重、压缩后的长期记忆

限制：

- 不允许原始堆砌几十条记忆
- 必须先做相关性筛选
- 必须控制条目数和单条长度

### 4.5 `TOOL_CATALOG`

内容：

- 当前轮真正可用的工具/能力摘要

建议字段：

- `name`
- `description`
- `usage`
- `risk`

限制：

- 不直接塞完整实现文档
- 不直接塞冗长插件说明
- 只保留模型做决策所需的最小信息

### 4.6 `SAFETY_OR_RUNTIME_RULES`

内容：

- 当前轮附加限制
- 例如禁止高风险能力、当前环境不支持某类调用

说明：

- 只在本轮确有必要时注入
- 不应替代 `SystemMessage`

## 5. 标准顺序

推荐固定顺序如下：

1. `SystemMessage`
2. `EnhanceMessage(CHARACTER_PROFILE)`
3. `EnhanceMessage(SAFETY_OR_RUNTIME_RULES)` 可选
4. `EnhanceMessage(RUNTIME_CONTEXT)` 可选
5. `EnhanceMessage(RESOURCE_CONTEXT)` 可选
6. `EnhanceMessage(LONG_TERM_MEMORY)` 可选
7. `EnhanceMessage(TOOL_CATALOG)` 可选
8. `ChatMessage[]`

要求：

- 最后一条 `ChatMessage` 通常必须是当前轮输入
- 如果当前轮是 tool continuation，则最后一条可为 `tool` 或 continuation 对应消息

## 6. 标准模板

```csharp
ChatMessage[] messages =
[
    new SystemMessage("""
你是一个角色 Agent。
你必须优先遵守 system 规则。
你可以参考后续结构化上下文块，但不要显式复述这些块名。
如果需要调用工具，先做最小必要决策。
如果无需工具，直接给出符合角色设定的回复。
"""),

    new EnhanceMessage(ChatMessageRole.User, "CHARACTER_PROFILE", """
名字：铃
身份：猫娘
核心设定：……
性格特征：……
说话风格：……
关系推进要求：……
"""),

    new EnhanceMessage(ChatMessageRole.User, "RUNTIME_CONTEXT", """
当前地点：玩家的家
当前时间：第 3 天，傍晚
当前事件：玩家刚刚晚归
"""),

    new EnhanceMessage(ChatMessageRole.User, "RESOURCE_CONTEXT", """
affection=420
trust=68
mood=anxious
relationship_stage=朋友
"""),

    new EnhanceMessage(ChatMessageRole.User, "LONG_TERM_MEMORY", """
- 你记得玩家曾在你生病时整夜照顾你。
- 你对“被忽视很久”非常敏感。
"""),

    new EnhanceMessage(ChatMessageRole.User, "TOOL_CATALOG", """
- offer_gift: 接收礼物并结算状态变化。risk=low
- ask_character: 向其他角色发问。risk=low
- open_inventory: 查看背包。risk=low
"""),

    new ChatMessage(ChatMessageRole.User, "昨天你是不是有点不开心？"),
    new ChatMessage(ChatMessageRole.Assistant, "……没有不开心，只是一直在等你。"),
    new ChatMessage(ChatMessageRole.User, "抱歉，我回来晚了。")
];
```

## 7. 内容预算

### 7.1 `SystemMessage`

- 300 到 800 字以内

### 7.2 `CHARACTER_PROFILE`

- 500 到 1500 字以内
- 只保留会影响当前行为的核心信息

### 7.3 `RUNTIME_CONTEXT`

- 200 到 600 字以内

### 7.4 `RESOURCE_CONTEXT`

- 5 到 20 项以内
- 只保留与本轮有关的资源

### 7.5 `LONG_TERM_MEMORY`

- 3 到 8 条
- 每条 40 到 120 字以内

### 7.6 `TOOL_CATALOG`

- 工具数量尽量收敛
- 每个工具 1 到 3 行摘要

### 7.7 `ChatMessage`

- 最近 4 到 12 条
- 更早历史应在进入此层之前摘要化

## 8. 裁剪优先级

优先保留：

1. `SystemMessage`
2. `CHARACTER_PROFILE`
3. 当前轮输入
4. 最近几轮短期历史
5. `RUNTIME_CONTEXT`
6. `RESOURCE_CONTEXT`
7. `LONG_TERM_MEMORY`
8. `TOOL_CATALOG`

token 紧张时，优先裁剪：

- 旧对话历史
- 长期记忆数量
- 工具描述长度
- 资源项数量

不要优先裁剪：

- system
- 角色核心设定
- 当前轮输入

## 9. CharacterAgent 组装职责

CharacterAgent 侧推荐按以下责任分层：

- `SystemMessageBuilder`
  - 只生成稳定的全局 system 规则
- `CharacterProfileBuilder`
  - 从 `backgrounds` 生成 `CHARACTER_PROFILE`
- `RuntimeContextBuilder`
  - 从当前会话、场景、调度信息生成 `RUNTIME_CONTEXT`
- `ResourceContextBuilder`
  - 从状态系统挑选与本轮相关的资源生成 `RESOURCE_CONTEXT`
- `LongTermMemoryBuilder`
  - 从记忆系统检索、压缩后生成 `LONG_TERM_MEMORY`
- `ToolCatalogBuilder`
  - 从当前 capability snapshot 生成 `TOOL_CATALOG`
- `ShortTermHistoryBuilder`
  - 从最近会话轮次生成 `ChatMessage[]`

最终由统一的 CharacterAgent Prompt Builder 按顺序合并输出。

## 10. 与插件 / 资源系统的关系

插件与资源系统不应再直接把原始 registry、原始 state snapshot 或完整插件文档无差别塞进模型输入。

正确做法是：

- 插件注册资源仍通过 `StateRegistry`
- Host 读取当前值与元数据
- 经过选择、裁剪、摘要后，映射为 `RESOURCE_CONTEXT`

如果插件希望补充语义：

- 可以继续通过 hook 产出 prompt fragment
- 但 fragment 最终应被归并进 `RUNTIME_CONTEXT` 或 `RESOURCE_CONTEXT`
- 不应直接在 CharacterAgent 主链里保留另一套并行 prompt 拼接逻辑

## 11. 明确废弃的旧方案

以下方案应视为旧方案，不再作为 CharacterAgent 的目标形态：

- 将角色画像、工具目录、资源状态、对话历史统一压成一个 JSON object
- 再把该 JSON object 作为单条 `user` 消息发给模型

废弃原因：

- 违背 chat 模型原生消息语义
- 规则、上下文、历史三类信息混杂
- 难以做独立裁剪
- `EnhanceMessage` / `SystemMessage` 无法发挥作用

## 12. 当前迁移目标

第一阶段最小迁移目标：

- `SystemMessage`
- `EnhanceMessage(CHARACTER_PROFILE)`
- `EnhanceMessage(RUNTIME_CONTEXT)`
- `EnhanceMessage(RESOURCE_CONTEXT)`
- `EnhanceMessage(LONG_TERM_MEMORY)`
- `EnhanceMessage(TOOL_CATALOG)`
- 最近几轮 `ChatMessage`

等这套输入形态稳定后，再继续调整：

- 哪些插件 hook 输出归入 `RUNTIME_CONTEXT`
- 哪些状态进入 `RESOURCE_CONTEXT`
- 哪些长期记忆应继续压缩
