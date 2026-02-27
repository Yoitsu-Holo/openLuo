# Mod / Pack 规范

本文档不再把 mod 仅仅定义为“物品扩展文件”。  
新架构下，mod 应被提升为更一般的 **content pack**。

## 1. 当前代码事实

现状：

- `ItemContentPackLoader.LoadAll` 读取 `openLuo/data/item-packs/**/*.jsonc`
- 当前主要消费物品目录

这说明旧 mod 体系还停留在：

- 物品内容包

而不是：

- 通用内容扩展包

## 2. 新定位

建议把 mod 的统一抽象改为：

## `Content Pack`

一个 pack 可以提供：

- items
- character archetypes
- resource defs
- tool defs
- skill defs
- flows
- world facts

## 3. PackManifest

每个 pack 应有统一 manifest：

```text
id
version
kind
schemaVersion
dependencies
provides
overridePolicy
metadata
```

其中：

- `kind` 可为 `content-pack`
- `dependencies` 用于声明依赖其它 pack
- `provides` 用于说明提供哪些定义类型

## 4. ItemDefinition

旧 `mods/*.jsonc` 中的 item 可迁移为新的 `ItemDefinition`：

可落地字段模板见：

- [content-schema.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/content-schema.md) 中的 `ItemDefinition`

### 内容字段

- `id`
- `displayName`
- `description`
- `category`
- `rarity`
- `tags`

### 规则字段

- `price`
- `stackable`
- `ownership rules`

### 效果字段

不建议继续把：

- `affectionDelta`
- `moodEffect`

作为顶层固定字段长期保留。建议改成：

```text
effects[]
```

每条 effect 至少包括：

- `type`
- `target`
- `op`
- `value`
- `conditions`

## 5. 兼容策略

短期内可以允许：

- 旧 `affectionDelta`
- 旧 `moodEffect`

在加载阶段转换成 `effects[]`。

这样旧内容不需要一次性全部推翻，但新 schema 已经明确。

## 6. 结论

后续不应再把 mod 理解为“物品文件夹”。  
它应成为统一的内容扩展包抽象，为 `mod / DLC / builtin pack` 提供共通基础。
