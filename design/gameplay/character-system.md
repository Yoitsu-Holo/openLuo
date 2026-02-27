# 角色系统

## 1. 当前角色来源

当前角色由背景定义驱动。

初始化游戏时：

- 选中的背景会成为当前主背景
- 所有已加载背景会被写入 `characters` 表
- 每个背景生成一个角色记录

角色 ID 规则当前为：

- `char_<background-id-normalized>`

## 2. 当前角色字段

角色记录至少包含：

- `id`
- `game_id`
- `archetype_id`
- `name`
- `display_priority`
- `is_enabled`

## 3. 当前角色运行时

角色不是静态资料，而是拥有 runtime 的行为体。

运行时特征：

- 由 `AgentRuntimeHub` 统一预热
- 每个角色拥有 mailbox
- 角色通过 `DefaultAgentMessageHandler` 处理消息
- 角色能接受玩家消息、任务分配和角色间询问

## 4. 当前角色能力

每个角色在逻辑上可获得三类能力：

- 插件能力
- 核心 companion 能力
- inter-agent 能力

实际暴露面由 `UnifiedAgentCapabilityRegistry` 统一生成。

## 5. 当前 active character 机制

- `GameState.ActiveCharacterId` 保存当前主动角色
- `--as` 参数可覆盖当前角色选择
- 成功命中后会把当前 active character 写回存档

## 6. 当前多角色协作能力

- `/task`：给多个角色分发协作任务
- `ask_character`：一个角色向另一角色发问
- `chat_with_character_session`：两个角色进行内部对话 session

## 7. 当前限制

- 玩家主会话和内部会话尚未完全做线程隔离
- 角色长期成长与记忆层次仍较轻量
- 角色资料主要集中在背景文件，尚未独立成更强配置模型
