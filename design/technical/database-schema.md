# 数据库结构

当前数据库由 `Infrastructure/Database/DatabaseInitializer.cs` 初始化和迁移，底层为 SQLite。

## 1. 核心表

### game_state

用途：

- 当前存档主状态
- 玩家名
- 当前背景
- 当前激活角色
- 当前天数、分钟、地点

### characters

用途：

- 当前存档下的角色清单
- 角色与背景绑定
- 展示顺序、启用状态
- 角色 profile/policy JSON 扩展位

### affection_events

用途：

- 角色好感事件日志

## 2. 记忆相关

### memories

用途：

- 结构化保存角色记忆文本
- 带 `game_id`、`character_id`、情绪权重、发生时间和压缩标记

### vec_memories

用途：

- `sqlite-vec` 向量表
- 当前按 `memory_id + game_id + character_id + embedding` 存储

## 3. 世界状态相关

### state_defs

用途：

- 注册状态定义
- 包含 namespace、key、owner kind、类型、默认值、枚举值、元数据等

### state_store

用途：

- 保存当前状态值

### timeline_events

用途：

- 保存时间线待执行事件

## 4. 资产相关

典型表包括：

- 资产记录
- 资产 blob
- 资产 meta
- 资产 link
- 资产 unlock

这些表由 `Assets` 子系统配合初始化与访问实现维护。

## 5. 任务与协作相关

当前还有 party task 相关表用于：

- 创建任务
- 跟踪任务步骤
- 保存角色协作结果

## 6. 设计特点

- 以单 SQLite 文件承载当前游戏核心状态。
- 面向“单实例宿主 + 多存档扩展”演进，多个表已显式带 `game_id`。
- 向量记忆与普通记忆分表，便于迁移与降级。

## 7. 当前注意点

- `DatabaseInitializer` 同时承担建表、迁移和 `sqlite-vec` 校验，是数据库演进入口。
- 任何新增表或字段都应同步补充迁移逻辑与测试。
