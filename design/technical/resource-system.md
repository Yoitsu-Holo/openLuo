# 资源系统

本文档描述内容资源在新架构下的组织方式。重点不是复述当前 `data/` 目录如何被老 loader 读取，而是说明资源如何优雅进入新的 Agent / Executor / Runtime 体系。

## 1. 资源系统目标

资源系统要同时支持：

- builtin 内容
- mod 内容
- plugin 提供的资源
- DLC 内容
- 角色、物品、资源、工具、技能的自定义扩展

因此资源系统必须满足：

- schema 明确
- namespace 明确
- pack 级依赖与版本化
- registry 编译
- runtime materialization
- 宿主只持有最小 canonical 字段，不把扩展策略全部吸收到 C# 类型层

## 2. 三层组织

### Content Plane

定义内容本身：

- CharacterArchetypeDefinition
- ResourceDefinition
- ItemDefinition
- ToolDefinition
- SkillDefinition
- FlowDefinition
- WorldFactDefinition

### Extension Plane

定义内容由谁提供：

- builtin pack
- mod pack
- DLC pack
- runtime plugin

### Runtime Plane

定义如何把内容变成真实实例：

- registry build
- session bootstrap
- state seed
- memory seed
- capability bind
- flow bind

## 3. 当前 `data/` 目录的定位

当前 `openLuo/data/` 目录仍然是过渡状态，其内容可以视为旧载体：

- `backgrounds/`
  角色原型定义的旧载体
- `mods/`
  物品定义的旧载体
- `plugins/`
  runtime plugin 与部分旧资源定义的混合载体
- `skills/`
  skill 文档资产
- `tools/`
  tool 文档资产
- `subagents/`
  子代理说明资产

这意味着：

- 当前目录结构还能继续用
- 但不应直接等于长期 canonical schema
- `plugins/` 也不应被理解为唯一扩展入口

## 4. 当前代码事实

截至当前代码基线：

- `CharacterArchetypeLoader.LoadAll` 直接读取 `data/archetypes/*.jsonc`
- `ItemContentPackLoader.LoadAll` 直接读取 `data/item-packs/**/*.jsonc`
- `McpPluginHost.LoadAllAsync` 扫描 `data/plugins/*/plugin.jsonc`
- plugin runtime 已支持注册 `AgentFlowRegistration`

这些入口说明：

- 现有系统已经能读资源
- 但“能读”不等于“装配边界合理”

## 5. 新架构方向

资源系统后续应演进为：

```text
raw files
-> pack manifest
-> schema validation
-> content registry build
-> session bootstrap
-> runtime consumption
```

核心新增层：

- `PackManifest`
- `ContentRegistryBuilder`
- `SessionBootstrapper`

这三层的职责是把内容装配收口到宿主最小规则，而不是替代 Python / MCP 侧的扩展实现。

## 6. 角色卡与插件配置边界

这是资源系统里最重要的一条边界：

- 角色卡只负责角色背景设定
- mood / relationship / daily / inventory 等系统差异由插件配置承载
- 若没有角色级插件配置，则回退到插件默认配置

例子：

```text
plugins/builtin_mood/
  defaults.jsonc
  characters/
    rin.jsonc
```

## 7. 当前主要问题

- 角色卡职责过重，混入了太多系统特化参数
- mod 仍然基本等同于物品定义
- plugin 既承载代码扩展，又夹带资源定义
- tools / skills 还是文档资产，尚未完全升格成结构化定义
- 缺少统一 registry 编译层
- 对“新增扩展是否必须先改 C#”的边界仍不够清晰

## 8. 运行时资源接口

当前第一阶段已经把 `StateDef` / `StateValue` 上方收口出一层资源视图接口。

### 8.1 底层状态接口

底层接口仍保留：

- `IStateRegistry`
- `IStateQueryService`
- `IStateMutationService`

这些接口负责状态定义注册、状态值查询、状态值修改。它们仍然是 WorldState 的基础设施，不直接承担 UI 展示协议。

### 8.2 上层资源接口

新增资源视图接口：

- `IResourceCatalogService`
- `IResourceValueService`
- `IResourceStatusProjectionService`
- `IResourceEvaluationProjectionService`
- `IResourceLifecycleService`

职责分别是：

- 资源目录：枚举和查询当前注册资源定义
- 资源值：用资源语义读取状态值
- 状态投影：把资源定义和值投影为状态面板可消费的 `ResourceStatusSnapshot`
- 评估投影：把资源定义和值投影为 LLM 状态结算可消费的 `ResourceEvaluationSnapshot`
- 生命周期：更新资源定义的运行时生命周期状态

`StatusAggregator` 已调整为 `IResourceStatusProjectionService` 的兼容包装层，用于保持 Session/TUI 现有调用不变。

`StateEvaluationCoordinator` 已调整为 `IResourceEvaluationProjectionService` 的消费者，不再直接扫描 `IStateRegistry` 来决定哪些资源进入状态结算。

### 8.3 GameBridge 资源视图 API

插件侧新增只读资源视图 API：

- `game/resource/definitions`
- `game/resource/status`
- `game/resource/values`
- `game/resource/lifecycle`

这些接口不会替代既有 `game/state/*`。`game/state/*` 仍然是状态注册、查询、写入的稳定底层 API；`game/resource/*` 只提供上层资源视图。

### 8.4 `/status` 动态化

`builtin_system_status` 不再硬编码 `gold`、`stamina`、`affection` 等字段，而是调用 `game/resource/status` 获取当前可展示资源。

这意味着：

- 新资源只要注册为 `hiddenFromStatus=false`，即可进入 `/status` 输出
- `statusGroup`、`statusOrder`、`displayFormat` 成为展示排序和格式的主要依据
- 插件 `onStatusQuery` 仍可覆盖或补充状态项

## 9. 资源生命周期

资源定义现在具有运行时生命周期字段：

- `active`
- `hidden`
- `frozen`
- `retired`

语义如下：

- `active`：正常展示、正常参与状态结算、允许写入
- `hidden`：不进入默认状态展示，但仍可读取，仍可参与结算
- `frozen`：保留读取和展示能力，但拒绝状态写入，不参与 LLM 结算
- `retired`：保留定义用于兼容旧值，不进入默认目录、不展示、不结算，写入会被拒绝

退役策略字段：

- `keep_value`
- `hide_value`
- `purge_value`

当前第一版只持久化策略，不自动清理旧值。自动 purge 应作为独立高危操作实现。

## 10. 插件禁用语义

`plugin.jsonc` 支持：

```json
{
  "disabled": true
}
```

禁用插件会影响：

- 插件进程不会启动
- 插件命令不会注册
- 插件 hook 不会执行
- 该插件的内容 manifest、默认配置、角色覆盖配置和 `state_defs.jsonc` 不再进入内容 registry

禁用插件不会自动：

- 删除数据库中已有资源定义
- 删除数据库中已有资源值
- 自动退役资源

资源是否显示、冻结或退役，应通过 `game/resource/lifecycle` 或后续管理命令显式处理。

## 11. 后续治理方向

资源接口已经覆盖目录、值、状态展示、状态结算和生命周期基础能力。

后续需要继续补齐：

- 高危清理操作：按资源定义清理旧值
- CLI/TUI 管理命令：查看和切换资源生命周期
- 插件停用时的可选策略编排：例如自动 frozen 或 retired，但默认不应自动清理

## 12. 推荐阅读

下一步内容 schema 设计见：

- [content-schema.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/content-schema.md)
- [content-registry-bootstrap.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/content-registry-bootstrap.md)
