# 叙事节拍

## 1. 当前叙事实现方式

当前叙事不是单一“剧情脚本播放器”，而是由多层共同构成：

- 角色 Agent 决策
- `narrative_chat` 渲染能力
- 插件 hooks
- 记忆检索
- 世界状态与时间线

## 2. 当前叙事入口

### 玩家聊天

- `/chat` 最终进入角色 Agent 主链
- 普通聊天优先调用 `narrative_chat`

### 插件钩子

常见 hook：

- `onStartup`
- `onChatBefore`
- `onNarrativeAfter`
- `onScheduleDue`
- `onStatusQuery`
- `onPromptContext`

### 时间与事件

- timeline 可创建和轮询到期事件
- 睡眠等生命周期命令可触发额外叙事处理

## 3. 当前节拍驱动因素

- 玩家输入
- 当前角色与 active character
- 最近对话
- 私有/共享记忆
- 插件插入的 prompt fragments
- 系统状态与时间

## 4. 当前特征

- 叙事已经不再只由单个插件主导。
- 角色人格、工具调用和叙事渲染已拆层。
- 叙事输出可同时受背景、状态、记忆和插件影响。

## 5. 当前限制

- 没有正式的剧情脚本编排层
- 高级剧情节拍仍更多依赖插件与提示词设计
- 长线叙事状态与角色内部线程还需要更细粒度管理
