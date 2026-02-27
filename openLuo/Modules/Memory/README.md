# Memory Module

`Memory` 是当前项目里的语义记忆底座。

它的职责只有三件事：

- 接收原始事件并写成结构化记忆
- 用语义查询召回结构化记忆
- 在向量检索失败时自动退回到词法检索

它不负责：

- Agent 编排
- Character prompt 拼接
- Gameplay 业务判断
- 历史兼容壳

当前模块优先为 executor 链路服务。
其他旧业务入口已经被显式切成 `TODO(memory)` 或空实现，后续是否重新接入再单独设计。

## 对外接口

### 写入

- `IMemoryWriteService`
  - 输入：`MemoryWriteInput`
  - 输出：`MemoryWriteResult`

调用方只需要描述：

- 这是哪个 `gameId`
- 属于哪个 `characterId`
- 原始事件文本是什么
- 记忆的作用域、情绪和重要度是什么

调用方不需要知道：

- 是否会生成 embedding
- 是否会写入 `vec_memories`
- 数据库存储字段如何组织

### 召回

- `IMemoryRecallService`
  - 输入：`SemanticRecallQuery`
  - 输出：`MemoryRecallResult`

调用方只需要描述：

- 搜索文本是什么
- 当前角色是谁
- 要查哪些 scope
- 是否偏好 recent / important / emotional

调用方不需要知道：

- 最终走的是 vector 还是 keyword fallback
- sqlite-vec 的 schema 是什么
- SQL 是怎么写的

## 分层

### `Core`

定义稳定的模块边界。

包含：

- 模型
  - `MemoryRecord`
  - `MemoryWriteInput`
  - `MemoryWriteResult`
  - `SemanticRecallQuery`
  - `MemoryRecallResult`
  - `MemorySnippet`
- 接口
  - `IMemoryWriteService`
  - `IMemoryRecallService`
  - `IMemoryWriteProjector`
  - `IMemoryRepository`
  - `IMemoryRetriever`

这里回答的问题是：

- 记忆长什么样
- 写入请求长什么样
- 召回请求长什么样

### `Application`

负责记忆生命周期编排。

包含：

- `DefaultMemoryWriteProjector`
- `MemoryCommitCoordinator`
- `MemoryRecallCoordinator`

这里回答的问题是：

- 原始事件如何投影成结构化记忆
- 写入时如何串起 projector / repository / embedding
- 召回时如何串起 retrieval pipeline

### `Infrastructure/Storage`

当前实现：

- `SqliteMemoryRepository`

职责：

- 把结构化记忆写入 `memories`
- 把向量写入 `vec_memories`
- 提供低层 recent 查询

它不负责：

- 选择检索策略
- 构建 prompt
- 业务级别的“该不该写入”

### `Infrastructure/Retrieval`

当前实现：

- `VectorMemoryRetriever`
- `KeywordMemoryRetriever`
- `CompositeMemoryRetriever`

职责：

- `VectorMemoryRetriever`
  - 优先走 sqlite-vec
- `KeywordMemoryRetriever`
  - 用 `recall_text / summary / source_text` 做词法召回
- `CompositeMemoryRetriever`
  - 先尝试 vector
  - 失败或无结果时回退到 keyword

## 存储模型

当前 `memories` 表不再使用旧的 `content` 语义模型。
模块内部使用的结构化字段是：

- `memory_format_version`
- `scope`
- `source_text`
- `summary`
- `recall_text`
- `tags_json`
- `entities_json`
- `metadata_json`
- `emotion`
- `importance`
- `salience`
- `emotional_weight`
- `occurred_at`
- `is_compressed`

其中三类文本字段的职责要明确区分：

- `source_text`
  - 原始事实文本
  - 更接近“发生了什么”
- `summary`
  - 给上层 prompt / snippet 展示用的压缩文本
  - 更接近“给模型看的摘要”
- `recall_text`
  - 给检索器工作的文本
  - 更接近“给搜索命中的语义表达”

## Context 归属

从当前系统视角看，这几个字段分别进入不同 context：

### 存储上下文

- `source_text`
- `summary`
- `recall_text`
- `tags_json`
- `entities_json`
- `metadata_json`
- `emotion`
- `importance`
- `salience`

这是数据库层的完整语义载荷。

### 检索上下文

主要使用：

- `recall_text`
- `summary`
- `source_text`
- `tags_json`

其中：

- 向量检索时，嵌入请求优先使用 `recall_text`
- 关键词检索时，优先匹配 `recall_text / summary / source_text`

### Executor 上下文

当前 executor 层主要消费：

- `summary`
- `tags`

也就是说，executor 看到的是整理后的语义摘要，而不是存储层原始行。

## 写入流程

当前写入链：

```text
MemoryWriteInput
-> IMemoryWriteProjector
-> MemoryRecord
-> SqliteMemoryRepository.StoreRecordAsync
-> optional: EmbedAsync
-> SqliteMemoryRepository.StoreEmbeddingAsync
```

当前默认写入策略：

- 保留 `source_text`
- 生成较短 `summary`
- 生成检索导向的 `recall_text`
- 生成轻量 `tags`

## 召回流程

当前召回链：

```text
SemanticRecallQuery
-> CompositeMemoryRetriever
   -> VectorMemoryRetriever
   -> fallback: KeywordMemoryRetriever
-> MemoryRecallResult
```

当前行为：

- vector 检索命中时直接返回
- vector 检索异常时记录 trace 并回退
- keyword 检索只依赖结构化字段，不再读旧的混合文本

## 当前设计取向

- 不做旧接口兼容壳
- 结构化字段优先于混合文本
- 检索策略可替换，但对外接口保持稳定
- executor 优先，其他业务入口延后重接

## 当前限制

- `DefaultMemoryWriteProjector` 仍是一个保守实现
  - `summary / recall_text / tags` 已经分工，但语义仍偏简单
- `salience` 目前仍偏向 retrieval-side 的粗略分数
- `entities_json` / `metadata_json` 已预留，但暂未深度利用

## 后续建议

下一步最值得继续收敛的是：

1. 强化 `DefaultMemoryWriteProjector`
2. 让 `summary / recall_text / tags` 的职责分离得更明显
3. 再决定是否把 Gameplay / Narrative / Agent 的记忆入口重新接回新接口
