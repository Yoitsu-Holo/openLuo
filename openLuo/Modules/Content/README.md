# Content Module

负责 canonical 内容定义、内容加载、校验与只读内容注册表。

包含：
- `Core/Definitions`：canonical content schema（`CharacterArchetypeDefinition`、`ItemDefinition`、`PackManifest` 等）
- `Application/Loaders`：`CharacterArchetypeLoader`、`ItemContentPackLoader`、`ToolDocLoader`、`SkillDocLoader`、`PluginContentLoader`
- `Application`：`CharacterArchetypeCatalog`、`ItemDefinitionCatalog`
- `Application/Validation`：最小 validator 接口与基础实现
- `Application/Registry`：只读 `ContentRegistry`、`RegistryEntry`、`ContentRegistryBuilder`

边界：
- 对外提供 archetype / item / resource / tool / skill 的 canonical 只读目录
- `data/archetypes` / `data/item-packs` 是当前 canonical 内容输入目录
- 不负责宿主装配
- 不直接承载玩法编排
