# 角色卡规范

本文档替代旧“背景规范”的定位。  
新架构下，`backgrounds/*.jsonc` 应被视为 **角色原型定义** 的旧载体，而不是全系统配置中心。

## 1. 核心原则

角色卡只描述角色本身，不描述所有子系统的行为特化。

角色卡负责：

- 身份与设定
- 人格与说话风格
- 外观与背景故事
- 静态叙事提示
- prompt 资产

角色卡不负责：

- mood 阈值
- intimacy 资源规则
- inventory 偏好参数
- daily 插件特化
- 资源 delta 策略

这些内容应下沉到对应插件配置，并支持：

- 默认配置
- 角色覆盖配置
- 无覆盖时默认回退

## 2. 新定位

建议将当前背景定义升级为：

## `CharacterArchetypeDefinition`

推荐字段分组：

### identity

- `id`
- `displayName`
- `aliases`
- `description`

### persona

- 核心性格
- 身份定位
- 社交风格
- 表达偏好

### appearance

- 概览
- 视觉特征
- 外观细节
- 可选绘图 prompt 资产

### backstory

- 背景故事
- 静态经历
- 世界内关系原点

### speechStyle

- 语气
- 句长倾向
- 直接程度
- 口头习惯

### narrativeAssets

- `narrativeHints`
- `dialogueExamples`
- 可选 `promptAssets`

### staticWorldBindings

- 默认地点
- 阵营
- 固定归属
- 静态关系槽位

可落地字段模板见：

- [content-schema.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/content-schema.md) 中的 `CharacterArchetypeDefinition`

## 3. 当前旧字段如何看待

当前 `backgrounds/*.jsonc` 中常见字段：

- `basePrompt`
- `personality`
- `likes`
- `dislikes`
- `habits`
- `narrativeHints`
- `dialogueExamples`
- `emotionalTriggers`
- `agentGoals`

处理建议：

- `basePrompt / personality / narrativeHints / dialogueExamples`
  继续保留为角色语义资产
- `likes / dislikes / habits`
  保留为角色设定内容
- `emotionalTriggers`
  视情况迁移到 mood 插件配置
- `agentGoals`
  若本质是角色长期目标，可保留；若是运行时策略，应迁移

## 4. 当前数据的使用方式

现状：

- 由 `CharacterArchetypeLoader` 读取
- 初始化角色表或 profile 时被消费
- Agent prompt 组合会引用这些内容

目标：

- 角色卡进入 `CharacterRegistry`
- 再由 `SessionBootstrapper` materialize 成角色实例

## 5. 结论

角色卡必须从“角色配置总线”收缩回“角色定义资产”。  
这一步是让插件系统和 mod/DLC 扩展真正可持续的前提。
