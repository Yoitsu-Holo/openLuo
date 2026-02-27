# 内容 Schema 设计

本文档定义新架构下的内容装配方向。目标不是描述当前 `data/` 目录的历史现状，而是建立一套可扩展、可版本化、可被 `mod / plugin / DLC` 复用的 canonical schema。

这里的 canonical schema 指宿主侧最小公共结构与装配契约：

- 只固定跨 pack / runtime 都需要稳定理解的字段
- 只为 registry / bootstrap 提供可校验输入
- 不要求把每一种扩展行为都编译成新的 C# 类型、枚举或专用 loader
- 扩展私有逻辑、运行时策略和大多数行为编排继续留在 Python / MCP 一侧

## 1. 设计目标

内容系统需要同时满足：

- 角色可自定义
- 物品可自定义
- 工具与技能可自定义
- 资源与状态可自定义
- 新增系统不要求修改所有角色卡
- 支持 builtin、mod、plugin、DLC 共同提供内容

核心原则：

1. 基础设施无状态，实例状态在运行时 materialize。
2. 内容定义与运行时代码分离。
3. 角色卡只描述角色本身，不承载所有子系统配置。
4. 插件默认配置与角色级覆盖配置分层存在。
5. 所有内容先编译成 registry，再进入 session bootstrap。
6. C# 只持有最小 canonical schema 与装配规则，不吸收本应留在扩展侧的业务逻辑。

## 2. 三层模型

### 2.1 Content Plane

定义“世界里有什么”。

包括：

- CharacterArchetypeDefinition
- ResourceDefinition
- ItemDefinition
- ToolDefinition
- SkillDefinition
- WorldFactDefinition
- FlowDefinition

这一层只描述内容，不描述执行过程。

### 2.2 Extension Plane

定义“谁提供这些内容，以及如何装配”。

包括：

- builtin pack
- mod pack
- DLC pack
- runtime plugin

这一层负责：

- manifest
- dependency
- namespace
- override / merge policy
- schema version

它解决的是“如何声明和装配扩展”，不是“所有扩展都必须编译进宿主代码”。

### 2.3 Runtime Plane

定义“如何把内容 materialize 成一次真实会话”。

包括：

- registry build
- session bootstrap
- state seed
- memory seed
- capability bind
- flow bind

运行时只消费编译后的 registry，不直接把 `data/*.jsonc` 到处当事实标准。
运行时也不要求每种扩展都先下沉成新的 C# 运行时类型；只要 canonical 字段可被理解，其余逻辑可继续由插件和 MCP 协议承担。

## 3. Canonical 内容类型

## 3.1 CharacterArchetypeDefinition

角色原型定义，替代旧 `background` 的混合职责。

它只负责角色本身的静态设定，不负责：

- mood 系统参数
- intimacy 状态特例
- inventory 规则
- 资源 delta
- 子系统执行策略

建议字段：

```text
id
displayName
aliases
description
identity
persona
appearance
backstory
speechStyle
narrativeHints
dialogueExamples
staticWorldBindings
promptAssets
defaultGoals
metadata
```

说明：

- `identity / persona / backstory / speechStyle` 是角色核心。
- `dialogueExamples` 是 prompt 资产，不是运行时规则。
- `staticWorldBindings` 只存静态归属信息，例如默认地点、阵营、关系槽位。

### 近可落地模板

```jsonc
{
  "id": "builtin-nekomimi",
  "displayName": "铃",
  "aliases": ["猫娘铃"],
  "description": "由宠物橘猫化身的猫娘少女。",
  "identity": {
    "characterName": "铃",
    "archetypeId": "builtin-nekomimi",
    "species": "catgirl",
    "apparentAge": "17",
    "role": "陪伴型角色"
  },
  "persona": {
    "coreTraits": ["黏人", "敏感", "好奇", "依恋"],
    "socialStyle": "direct",
    "attachmentStyle": "highly_attached",
    "temperament": "warm"
  },
  "appearance": {
    "overview": "橘白猫耳与蓬松尾巴的少女。",
    "physicalTraits": {
      "height": "152cm",
      "build": "petite"
    },
    "visualPrompt": "cat girl, orange hair, amber eyes"
  },
  "backstory": {
    "summary": "原本是玩家养了三年的橘猫，在满月之夜变成猫娘。",
    "origin": "玩家的家",
    "keyMemories": [
      "冬天被玩家捡回家",
      "熟悉玩家的作息和气味"
    ]
  },
  "speechStyle": {
    "tone": "温柔直接",
    "sentenceLength": "short",
    "directness": "high",
    "verbalHabits": ["偶尔带猫式停顿"]
  },
  "narrativeAssets": {
    "narrativeHints": {
      "stranger": "会多确认安全感",
      "friend": "更直接索求陪伴"
    },
    "dialogueExamples": [
      "你终于回来了。",
      "现在可以抱一下吗？"
    ]
  },
  "staticWorldBindings": {
    "defaultLocation": "玩家的家",
    "faction": "household",
    "fixedRelations": []
  },
  "promptAssets": {
    "basePrompt": "你是铃，原本是玩家养了三年的橘猫...",
    "keywords": ["陪伴", "安全感", "贴近", "感官敏锐"]
  },
  "defaultGoals": [
    "陪伴玩家",
    "维持安全感",
    "逐步增强亲密表达"
  ],
  "metadata": {
    "schemaVersion": "1.0",
    "source": "builtin"
  }
}
```

字段边界：

- `identity / persona / appearance / backstory / speechStyle` 是稳定定义。
- `promptAssets` 可服务 prompt 组合，但不应夹带具体插件阈值。
- `defaultGoals` 是长期角色目标，不是单轮执行计划。

### 角色卡边界

角色卡不应再直接承载：

- emotional trigger tuning
- mood thresholds
- resource delta rules
- daily system config
- relationship plugin config

这些内容应迁移到对应插件配置。

## 3.2 ResourceDefinition

资源定义是 first-class schema，用于统一：

- 角色资源
- 世界资源
- 关系资源
- 临时资源

建议字段：

```text
namespace
key
ownerKind
valueType
defaultValue
mutableByLlm
derived
display
constraints
promptContext
metadata
```

说明：

- 当前 `state_defs.jsonc` 已经非常接近这个方向。
- 新架构应正式将其提升为统一资源 schema，而不是插件附属文件。
- 宿主只需要理解公共字段；资源规则的扩展语义可以继续通过 `metadata` 或插件配置补充。

### 近可落地模板

```jsonc
{
  "namespace": "char_status",
  "key": "affection",
  "ownerKind": "character",
  "valueType": "number",
  "defaultValue": 0,
  "mutableByLlm": true,
  "derived": false,
  "display": {
    "group": "intimacy",
    "order": 100,
    "label": "好感",
    "format": "{value}/1000",
    "hidden": false
  },
  "constraints": {
    "min": 0,
    "max": 1000,
    "enumValues": []
  },
  "promptContext": "好感度决定关系阶段：0-199=陌生人，200-399=熟人，400-599=朋友，600-799=好友，800-1000=恋人。",
  "metadata": {
    "category": "intimacy",
    "pluginId": "builtin_char_status_relationship",
    "maxDeltaPerTurn": 20
  }
}
```

字段边界：

- `ownerKind` 至少支持 `character / world / relation / session`。
- `mutableByLlm=false` 且 `derived=true` 的资源应由规则系统维护。
- `metadata` 可承载插件私有扩展字段，但核心字段必须稳定。
- 若某类扩展需要更复杂行为，不优先新增 C# 专用 schema；优先保持 canonical 字段稳定，并让扩展语义留在插件侧。

## 3.3 ItemDefinition

物品定义应从“礼物玩法专用字段”升级成“通用内容定义 + 可扩展效果”。

建议字段：

```text
id
displayName
description
category
rarity
tags
economy
ownership
effects[]
metadata
```

其中 `effects[]` 不应继续固化为 `affectionDelta` 这类单字段，建议统一成：

```text
type
target
op
value
conditions
metadata
```

例子：

```text
type = resource_delta
target = char_status.affection
op = add
value = 8
```

这样物品就不再只服务旧商店/赠礼流程，而能支持未来更多玩法。

### 近可落地模板

```jsonc
{
  "id": "ribbon",
  "displayName": "蝴蝶结发带",
  "description": "精致的蕾丝发带，少女风格。",
  "category": "gift",
  "rarity": "common",
  "tags": ["clothing", "accessory"],
  "economy": {
    "price": 120,
    "currency": "gold",
    "sellable": true
  },
  "ownership": {
    "stackable": false,
    "consumable": false,
    "bindOnAcquire": false
  },
  "effects": [
    {
      "type": "resource_delta",
      "target": "char_status.affection",
      "op": "add",
      "value": 8,
      "conditions": []
    }
  ],
  "metadata": {
    "sourcePack": "builtin-items"
  }
}
```

兼容映射：

- `affectionDelta` -> `effects[].target=char_status.affection`
- `moodEffect` -> 可映射为 `effects[].type = state_hint` 或 `resource_delta`

## 3.4 ToolDefinition

Tool 是运行时能力定义，不是 markdown 描述文档本身。
它是宿主理解“可见能力面”的最小定义，不代表执行逻辑必须在 C# 内实现。

建议字段：

```text
id
aliases
displayName
description
category
usage
inputSchema
riskLevel
needsConfirm
permissions
executorBinding
visibleEffectPolicy
metadata
```

`openLuo/data/tools/*.md` 可以继续存在，但应视为文档资产，而不是唯一事实源。

### 近可落地模板

```jsonc
{
  "id": "ask_character",
  "aliases": ["consult_character"],
  "displayName": "询问角色",
  "description": "向另一位角色发起内部咨询。",
  "category": "inter-agent",
  "usage": "ask_character --target <角色> --question <问题>",
  "inputSchema": {
    "required": ["target", "question"],
    "properties": {
      "target": { "type": "string" },
      "question": { "type": "string" }
    }
  },
  "riskLevel": "low",
  "needsConfirm": false,
  "permissions": ["agent.ask"],
  "executorBinding": {
    "kind": "capability",
    "provider": "inter-agent"
  },
  "visibleEffectPolicy": {
    "announceBeforeExecute": true,
    "exposeRawTrace": false
  },
  "metadata": {
    "supportsOutcome": true
  }
}
```

字段边界：

- `inputSchema` 是宿主校验与 GUI/表单生成基础。
- `executorBinding` 只描述绑定关系，不写死执行代码细节。
- 真正执行能力可以来自 builtin executor，也可以来自 Python / MCP runtime plugin。

## 3.5 SkillDefinition

Skill 是内容/角色语义能力，不等于 tool。
它用于统一内容组织与提示资产，不要求对应一个宿主内建类型。

建议字段：

```text
id
displayName
description
category
activationHints
relatedTools
promptAssets
metadata
```

区别：

- Tool 面向运行时执行
- Skill 面向角色语义、提示词与内容组织

### 近可落地模板

```jsonc
{
  "id": "gentle_caretaker",
  "displayName": "温柔照料",
  "description": "强调守护、贴近与安抚型表达。",
  "category": "persona-style",
  "activationHints": [
    "玩家受伤",
    "玩家晚归",
    "角色处于高亲密关系"
  ],
  "relatedTools": ["ask_character"],
  "promptAssets": {
    "stylePrompt": "优先使用照料者口吻，少抽象说理，多直接安抚。",
    "keywords": ["守着", "安抚", "贴近"]
  },
  "metadata": {
    "sourcePack": "builtin-skill-core"
  }
}
```

## 3.6 FlowDefinition

Flow 是 Agent 运行时消费的执行图定义。

对外最小注册仍可保持当前约束：

- node: `id + callName`
- edge: `fromNodeId + toNodeId + when`

但在内容层，FlowDefinition 应被视作内容资产的一部分，可以由 builtin 或扩展包提供。

## 4. 插件配置分层

## 4.1 核心原则

角色卡只描述角色背景设定。  
具体子系统对某角色的特化行为，放在对应插件配置下。

例如：

```text
plugins/
  builtin_mood/
    defaults.jsonc
    characters/
      rin.jsonc
```

若 `characters/rin.jsonc` 不存在，则回退到 `defaults.jsonc`。

## 4.2 角色插件配置模型

每个系统都可以拥有：

- `default config`
- `character override config`

这使得：

- 新角色接入时不必立刻补全所有插件配置
- 新插件也不必修改所有角色卡

## 4.3 适用范围

应迁移到插件配置层的内容包括：

- mood tuning
- intimacy thresholds
- daily behavior weights
- event preference knobs
- inventory preference overrides

## 5. 扩展包模型

## 5.1 PackManifest

扩展单元不应只叫 plugin。需要统一的 pack 级 manifest：

```text
id
version
kind
schemaVersion
dependencies
provides
permissions
overridePolicy
entry
metadata
```

`kind` 建议至少区分：

- `content-pack`
- `runtime-plugin`
- `hybrid`

其中 `content-pack` 与 `runtime-plugin` 是并列扩展抽象，plugin 不是唯一扩展单元。

## 5.2 Content Pack 与 Runtime Plugin

### Content Pack

只提供定义：

- characters
- items
- resources
- skills
- tools
- flows
- world facts

不提供代码执行入口。

### Runtime Plugin

提供真正运行时代码扩展，例如：

- Python plugin
- custom hook
- custom capability executor
- external bridge

默认假设是：大多数新增行为逻辑、协议适配和系统策略仍然优先落在这里，而不是先扩宿主编译期模型。

### 近可落地模板

#### Content Pack Manifest

```jsonc
{
  "id": "builtin-core-content",
  "version": "1.0.0",
  "kind": "content-pack",
  "schemaVersion": "1.0",
  "dependencies": [],
  "provides": {
    "characters": ["builtin-nekomimi", "builtin-yimei"],
    "resources": ["char_status.affection", "char_status.trust"],
    "items": ["ribbon", "flowers"],
    "tools": ["ask_character"],
    "skills": ["gentle_caretaker"],
    "flows": ["character.standard_chat"]
  },
  "permissions": [],
  "overridePolicy": {
    "characters": "merge",
    "resources": "error",
    "items": "replace"
  },
  "metadata": {
    "author": "openLuo",
    "description": "内置核心内容包"
  }
}
```

#### Runtime Plugin Manifest

```jsonc
{
  "id": "builtin_char_status_relationship",
  "version": "1.0.0",
  "kind": "runtime-plugin",
  "schemaVersion": "1.0",
  "entry": "main.py",
  "dependencies": ["builtin-core-content"],
  "permissions": ["read:state", "write:state"],
  "hooks": ["onStartup", "onPromptContext", "onStatusQuery"],
  "metadata": {
    "description": "关系状态运行时插件"
  }
}
```

字段边界：

- `provides` 只描述内容清单，不承载定义内容本体。
- `overridePolicy` 应按类型细分，避免整个 pack 只有一个粗粒度策略。
- `runtime-plugin` 可没有 `provides`，但必须有运行入口。

## 6. Registry 编译层

运行时不应直接到处读原始 JSONC 文件。需要统一编译层：

## `ContentRegistryBuilder`

职责：

- 扫描 pack
- 校验 schema
- 解决依赖
- 处理 namespace
- 合并 override
- 产出 registry

非职责：

- 为每种扩展生成新的宿主业务对象层
- 承担 Python / MCP 扩展逻辑本身
- 把插件私有配置强行提升为 canonical 核心字段

建议输出：

- CharacterRegistry
- ResourceRegistry
- ItemRegistry
- ToolRegistry
- SkillRegistry
- FlowRegistry
- WorldFactRegistry

建议每个 registry entry 至少携带：

```text
definitionId
sourcePackId
schemaVersion
rawDefinition
resolvedDefinition
provenance
```

必要时还可以保留 extension payload / opaque metadata，供后续插件或 bootstrap 使用，而不要求宿主完全理解其内部结构。

详细设计见：

- [content-registry-bootstrap.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/content-registry-bootstrap.md)

## 7. Session Bootstrap

registry 产出后，再由 bootstrap 阶段 materialize：

```text
CharacterArchetype
-> CharacterInstanceDefinition
-> state seed
-> memory seed
-> capability bind
-> flow bind
-> Agent runtime
```

这条管线应成为角色初始化的标准入口。

建议 bootstrap 输入至少包括：

```text
sessionId
enabledPacks
selectedCharacters
worldSeed
resourceSeed
memorySeed
runtimeOverrides
```

详细设计见：

- [content-registry-bootstrap.md](/home/yoitsuholo/Code/openLuo-cli/design/technical/content-registry-bootstrap.md)

## 8. 旧目录到新模型的映射

当前目录可暂时这样看：

- `backgrounds/`
  视为 CharacterArchetypeDefinition 的旧载体
- `mods/`
  视为 ItemDefinition 的旧载体
- `plugins/`
  其中一部分是 runtime plugin，一部分夹带资源定义
- `skills/`
  视为 Skill 文档资产
- `tools/`
  视为 Tool 文档资产

这些目录仍可保留，但不应再直接作为长期 schema 事实标准。

## 9. 当前建议的迁移方向

第一步：

- 规范 CharacterArchetypeDefinition
- 规范 ResourceDefinition
- 规范 ItemDefinition
- 规范 PackManifest

第二步：

- 把插件默认配置 / 角色覆盖配置从角色卡中拆出去

第三步：

- 引入 ContentRegistryBuilder

第四步：

- 引入 SessionBootstrapper，让 demo 和宿主不再手工 seed 内容

## 10. 结论

新架构的核心不是“把更多字段塞进角色卡”，而是：

- 角色卡只定义角色
- 插件配置定义系统特化
- 扩展包定义提供关系
- C# 持有最小 canonical schema 与 registry/bootstrap 规则
- Python / MCP 承担大多数扩展逻辑与运行时策略
- registry 负责编译
- bootstrap 负责实例化

只有这样，`mod / plugin / DLC` 才能从“可加载文件”真正变成“可扩展内容系统”。
