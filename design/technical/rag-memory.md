# RAG 记忆系统

## 1. 目标

当前 Memory 模块用于提供一个干净的语义记忆底座：

- 结构化写入
- 结构化召回
- sqlite-vec 向量检索
- keyword / fuzzy 回退

它当前主要服务 executor 链路，而不是继续扩散回旧业务入口。

## 2. 当前实现位置

核心实现分散在这些组件中：

- `openLuo/Modules/Memory/Application/MemoryCommitCoordinator.cs`
- `openLuo/Modules/Memory/Application/MemoryRecallCoordinator.cs`
- `openLuo/Modules/Memory/Infrastructure/Storage/SqliteMemoryRepository.cs`
- `openLuo/Modules/Memory/Infrastructure/Retrieval/VectorMemoryRetriever.cs`
- `openLuo/Modules/Memory/Infrastructure/Retrieval/KeywordMemoryRetriever.cs`
- `openLuo/Modules/Memory/Infrastructure/Retrieval/CompositeMemoryRetriever.cs`

## 3. 存储模型

`memories` 表现在保存结构化语义字段：

- `source_text`
- `summary`
- `recall_text`
- `tags_json`
- `entities_json`
- `metadata_json`
- `emotion`
- `importance`
- `salience`
- `scope`

`vec_memories` 表继续保存向量索引：

- `memory_id`
- `game_id`
- `character_id`
- `embedding`

## 4. 写入流程

```text
MemoryWriteInput
-> DefaultMemoryWriteProjector
-> MemoryRecord
-> SqliteMemoryRepository.StoreRecordAsync
-> optional EmbedAsync
-> SqliteMemoryRepository.StoreEmbeddingAsync
```

设计取向：

- 先保留结构化记忆本体
- embedding 失败不阻断主流程
- 向量只是增强层，不是唯一依赖

## 5. 检索流程

### Vector path

- `VectorMemoryRetriever`
- 基于 `SemanticRecallQuery` 构建搜索文本
- 调用 embedding
- 通过 sqlite-vec 命中 `vec_memories`
- 再 join 回 `memories`

### Keyword fallback

- `KeywordMemoryRetriever`
- 基于 `recall_text / summary / source_text` 做词法匹配
- 再做简单的 fuzzy 排序

### Composite path

- `CompositeMemoryRetriever`
- 先尝试 vector
- 无结果或异常时回退 keyword
- trace 中显式记录回退原因

## 6. 字段职责

- `source_text`
  - 原始事实
- `summary`
  - 上层 prompt / snippet 展示用文本
- `recall_text`
  - 检索器工作文本
- `tags_json`
  - 标签辅助检索

## 7. 当前风险

- `DefaultMemoryWriteProjector` 仍较保守
- `summary / recall_text / tags` 的语义仍可继续拉开
- `salience` 仍偏粗略
- 压缩与分层记忆体系尚未重建
