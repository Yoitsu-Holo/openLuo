# 输入流与运行时数据流

本文档描述当前新架构下，从内容输入到角色运行时的主要数据流转路径。

目标不是解释所有实现细节，而是明确：

- 新内容入口在哪里
- 会话初始化怎么进入运行时
- 玩家输入如何进入 Agent
- 多角色协作和插件配置如何参与

## 1. 总览

当前系统已经形成两条主入口：

1. **内容/初始化入口**
   - `raw content -> ContentRegistry -> SessionBootstrapper -> persisted runtime state`
2. **对话/运行时入口**
   - `session input -> GameSessionRuntime -> GameEngine -> AgentRuntime -> CharacterAgent`

这意味着：

- 新 schema 模式已经不再只是文档概念
- 新内容装配已经开始接管初始化链路
- 角色运行时也已经开始消费新输入流的结果

## 2. 内容装配流

原始内容来源现在直接进入 canonical schema 编译层，再进入统一 registry。

```mermaid
flowchart TD
    A[data/archetypes] --> B[CharacterArchetypeLoader]
    A2[data/item-packs] --> C[ItemContentPackLoader]
    A3[data/plugins plugin.jsonc state_defs defaults characters] --> D[PluginContentLoader]
    A4[data/tools md] --> E[ToolDocLoader]
    A5[data/skills md] --> F[SkillDocLoader]

    B --> G[canonical definitions]
    C --> G
    D --> G
    E --> G
    F --> G
    G --> H[ContentRegistryBuilder]
    H --> I[ContentRegistry]
```

说明：

- `backgrounds / mods / plugins / tools / skills` 已经不再绕过 registry 直接喂业务
- 运行时与业务层消费的事实源是 `ContentRegistry`

## 3. 插件配置合并流

插件默认配置与角色覆盖配置已经是独立输入流的一部分。

```mermaid
flowchart TD
    A[data/plugins/*/defaults.jsonc] --> B[PluginContentLoader]
    A2[data/plugins/*/characters/<id>.jsonc] --> B
    A3[data/plugins/*/config/*.jsonc] --> B
    B --> C[PluginDefaultConfigDefinition]
    B --> D[PluginCharacterConfigOverrideDefinition]
    C --> E[ContentRegistry]
    D --> E
    E --> F[merged plugin config]
```

当前支持：

- `defaults.jsonc`
- `characters/<id>.jsonc`

## 4. 会话初始化流

`SessionBootstrapper` 现在已经不仅返回只读结果，还会真正写入初始化状态。

```mermaid
flowchart TD
    A[ContentRegistry] --> B[SessionBootstrapper]
    B --> C[GameStateRepository]
    B --> D[CharacterRepository]
    B --> E[StateStore]
    B --> F[MemoryWriteService]
    B --> G[SessionBootstrapResult]
```

Bootstrap 当前负责：

- materialize 角色实例
- 选择 active character
- 写入 `GameState`
- 写入角色记录
- seed 资源状态
- 写入 shared/private memory
- 输出 diagnostics

## 5. Session Runtime 入口

当前 `GameSessionRuntime` 已经开始接入 bootstrap。

### 5.1 Open Session

```mermaid
flowchart TD
    A[Client Open Session] --> B[GameSessionRuntime.OpenAsync]
    B --> C[SessionRegistry create handle]

    C --> D{metadata 是否包含 archetypeId}
    D -- yes --> E[SessionBootstrapper.BootstrapAsync]
    D -- no --> F[只创建 session handle]

    E --> G[写 GameState / Characters / State / Memory]
    G --> H[RuntimeHub EnsurePartyStarted]
```

### 5.2 Initialize Game

```mermaid
flowchart TD
    A[InitializeGameAsync] --> B[SessionBootstrapper]
    B --> C[GameStateRepository]
    B --> D[CharacterRepository]
    B --> E[StateStore]
    B --> F[MemoryWriteService]
    F --> G[AgentRuntimeHub EnsurePartyStarted]
```

说明：

- `InitializeGameAsync` 已改走 bootstrap
- `OpenAsync` 也支持通过 metadata 直接触发初始化

## 6. 玩家输入到角色运行时

一旦 session 初始化完成，玩家输入就会进入标准运行时主链：

```mermaid
flowchart TD
    A[SessionInput] --> B[GameSessionRuntime.SubmitAsync]
    B --> C[InputRouter]
    C --> D[GameEngine.ExecuteAsync]
    D --> E[AgentInvocationRouter]
    E --> F[PlayerChatDispatcher]
    F --> G[AgentRuntimeHub]
    G --> H[AgentDispatcher]
    H --> I[DefaultAgentMessageHandler]
    I --> J[CharacterAgent]
    J --> K[AgentFlowRunner]
    K --> L[character.standard_chat]
```

## 7. 角色回合内部流

当前标准角色回合 flow：

```mermaid
flowchart TD
    A[character.standard_chat] --> B[memoryRecall]
    B --> C[plan]
    C --> D[plannedExecution]
    D --> E[stateUpdate]
    E --> F[turnResult]
```

其中 `plannedExecution` 内部是局部复合执行器：

```mermaid
flowchart TD
    A[PlannedExecutionPlanExecutor] --> B[step list]
    B --> C[pre_action_response]
    C --> D[tool_use loop]
    D --> E[final_response]
```

## 8. 多角色协作流

角色间内部咨询已经是标准运行时链的一部分：

```mermaid
flowchart TD
    A[Rin tool_use ask_character] --> B[InterAgentMessenger]
    B --> C[AgentRuntimeHub.RequestAsync AgentAsk]
    C --> D[Aliya CharacterAgent]
    D --> E[character.agent_ask]
    E --> F[finalize reply + inter-agent outcome]
    F --> G[InterAgentAskResult]
    G --> H[tool result 回到 Rin]
    H --> I[Rin final_response]
    I --> J[Rin state_update]
```

这条链说明：

- 角色间调用不是底层 LLM 直连
- 被咨询角色仍然作为完整 Agent 被调度
- 内部咨询结果会回到调用角色，再进入最终回复和状态结算

## 9. 新内容装配结果的运行时消费点

当前新输入流已经开始进入运行时消费面，不再只停留在 bootstrap。

```mermaid
flowchart TD
    A[ContentRegistry] --> B[SessionBootstrapper]
    A --> C[AgentProfileCatalog]
    A --> D[CosplaySkillProvider]
    A --> E[StatusAggregator]
    A --> F[StateEvaluationCoordinator]

    E --> G[onStatusQuery hook input]
    F --> H[onPromptContext hook input]
```

目前已接入的消费点包括：

- `CharacterArchetypeAgentProfileCatalog`
- `BackgroundCosplaySkillProvider`
- `StatusAggregator`
- `StateEvaluationCoordinator`

并且：

- merged plugin config 已经会进入 `onPromptContext`
- merged plugin config 已经会进入 `onStatusQuery`

## 10. 当前状态判断

当前不是“旧链路完全消失”的状态，而是：

### 已开始接管的部分

- content registry
- plugin config merge
- session bootstrap
- runtime hook input
- agent profile / cosplay skill 输入

### 当前剩余的历史约束

- SQLite 持久化列名仍沿用 `archetype_id`
- `data/archetypes` 与 `data/item-packs` 目录名尚未重命名

所以现在最准确的判断是：

**新输入流已经成为主事实源；当前剩余的是少量存储层和目录层的历史命名。**

## 11. 下一步建议

接下来最自然的工作顺序是：

1. 评估是否需要重命名 SQLite 列 `archetype_id`
2. 评估是否需要把 `data/archetypes` / `data/item-packs` 调整为更中性的目录命名
3. 在后续新功能中禁止重新引入旧内容模型和旧调用链

这三步做完后，新 schema 模式就会彻底只剩历史命名债，而不再存在旧架构调用链。
