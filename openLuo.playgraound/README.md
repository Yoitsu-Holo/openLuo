# openLuo.playgraound

最小 demo 工程。

用途：
- 单独验证 `openLuo` 内部模块
- 用最小方式演示模块调用
- 避免把实验代码写回主工程

当前 demo：
- `Demos/Llm/MicrosoftAiLlmClientDemo.cs`
- `Demos/Agent/CharacterAgentDemo.cs`
  - 当前链路：`CharacterAgent -> AgentFlowRunner -> character.standard_chat -> planned_execution(resp -> toolUse -> resp) -> stateUpdate`
  - 当前场景：玩家请求 汐泠 通过 `ask_character` 去问艾莉娅花园钥匙的位置
- `Demos/Executor/MemoryRecallExecutorDemo.cs`
- `Demos/Executor/MemoryKeywordFallbackDemo.cs`
- `Demos/Executor/MemoryVectorDemo.cs`
- `Demos/Executor/CharacterTurnPipelineDemo.cs`
  - 当前链路：`memoryRecall -> plan -> flowCheck? -> charResp -> statusUpdate`
- `Demos/Executor/FlowRoutingExecutorDemo.cs`
  - 单独验证：`FlowRoutingExecutor` 在候选边中选择下一步
- `Demos/Agent/SubgraphFlowDemo.cs`
  - 单独验证：`flow.subgraph` 调用另一个已注册子图
- `Demos/Agent/TurnMessageEmitterDemo.cs`
  - 单独验证：节点产出的 `TurnMessage -> OutputEventBus -> MessageOutput / TurnCompleted`
- `Demos/Content/ContentBootstrapDemo.cs`
  - 单独验证：`raw content -> ContentRegistry -> PluginConfigMerge -> SessionBootstrapper -> persisted state`
- `Demos/Plugin/ToolExecutedHookDemo.cs`
  - 单独验证：`host -> McpPluginHost -> onToolExecuted -> demo plugin`

配置：

```bash
cp openLuo.playgraound/config/llm.demo.example.ini openLuo.playgraound/config/llm.demo.ini
cp openLuo.playgraound/config/embedding.demo.example.ini openLuo.playgraound/config/embedding.demo.ini
```

默认示例已经切到 Ollama：

- `provider = Ollama`
- `baseUrl = http://localhost:11434`
- `apiKey` 可留空
- `model = qwen3:8b`

如果你改成远程 provider，再填写你自己的 `apiKey`。

如果你要验证真实的记忆向量检索，再额外配置：

```bash
cp openLuo.playgraound/config/embedding.demo.example.ini openLuo.playgraound/config/embedding.demo.ini
```

`embedding.demo.ini` 需要填写：

- `embedding.provider`
- `embedding.baseUrl`
- `embedding.apiKey`
- `embedding.model`
- `sqliteVec.vectorDimensions`
- `demo.requestDelayMs`

注意：

- `sqliteVec.vectorDimensions` 必须和 embedding 模型实际输出维度一致
- `demo.requestDelayMs` 用来控制多次 embedding 调用之间的等待时间；如果你用的是外部 API，建议先从 `1200` 到 `3000` ms 之间试起
- `memory-vector` demo 会先做一次向量维度探测；如果不一致，会直接报错并提示你该填什么值

运行示例：

```bash
dotnet run --project openLuo.playgraound
```

显式指定 demo：

```bash
dotnet run --project openLuo.playgraound -- character-agent
dotnet run --project openLuo.playgraound -- memory-recall
dotnet run --project openLuo.playgraound -- memory-fallback
dotnet run --project openLuo.playgraound -- memory-vector
dotnet run --project openLuo.playgraound -- pipeline
dotnet run --project openLuo.playgraound -- flow-routing
dotnet run --project openLuo.playgraound -- subgraph
dotnet run --project openLuo.playgraound -- turn-message
dotnet run --project openLuo.playgraound -- content-bootstrap
dotnet run --project openLuo.playgraound -- tool-hook
dotnet run --project openLuo.playgraound -- llm
```

## Trigger Tool Hook

如果你要验证新的插件工具观察 hook，运行：

```bash
dotnet run --project openLuo.playgraound -- tool-hook
```

这个 demo 不依赖 LLM，也不依赖完整游戏链。

它会：

- 启动一个最小 demo plugin
- 直接调用宿主的 `onToolExecuted` 分发
- 打印插件返回的 `additionalText` 和 `notices`

这个 demo 的目的就是回答：

- `onToolExecuted` 是否已经接通
- typed hook 的宿主分发是否正常
- `hooks/call` 与兼容结构是否工作正常

## Trigger Chat

如果你要触发当前的 Agent 对话 demo，直接运行：

```bash
dotnet run --project openLuo.playgraound -- character-agent
```

这个 demo 会自动构造一轮玩家输入，不需要再手动传参。当前固定输入是：

```text
帮我问一下艾莉娅，她记不记得花园钥匙放在哪里。
```

预期行为：

- `CharacterAgent` 先进行 `plan`
- 然后进入一个局部复合执行节点
- 这个节点会先做一次动作前确认 `resp`
- 再执行 `toolUse`
- 最后基于工具结果生成最终角色回复
- 最后执行 `stateUpdate`

输出中你应该重点看这几段：

- `=== Plan ===`
- `=== Tool Steps ===`
- `=== Final Reply ===`
- `=== State Update ===`

注意：

- 这个 demo 现在验证的是 Agent 主流程和真实 inter-agent 闭环
- `ask_character` 会真实触发 `汐泠 -> InterAgentMessenger -> 艾莉娅 -> AgentReply -> 汐泠`
- 如果你看到 LLM 请求失败，先检查 `openLuo.playgraound/config/llm.demo.ini` 以及本地代理/端口设置

## Trigger Subgraph

如果你要验证子图骨架，运行：

```bash
dotnet run --project openLuo.playgraound -- subgraph
```

这个 demo 不依赖 LLM。它会：

- 注册一个父图 `demo.parent`
- 注册一个子图 `demo.child`
- 通过 `flow.subgraph` 节点调用子图
- 把子图输出回写到父图 state

## Trigger Turn Message

如果你要单独验证新的消息流发射器，运行：

```bash
dotnet run --project openLuo.playgraound -- turn-message
```

这个 demo 不依赖 LLM。它会直接：

- 构造一条 `TurnMessageKind.Message`
- 发布到 `OutputEventBusTurnMessageEmitter`
- 再发布一条 `TurnMessageKind.Completed`
- 打印最终落到 event bus 中的 `MessageOutput / TurnCompleted`

这个 demo 的目的就是回答：

- 节点侧消息发射协议是否通了
- `msgFlow` 是否已经能独立于最终 `CommandResult` 工作
- EOF/完成事件是否已经具备明确语义

## Trigger Content Bootstrap

如果你要验证新的内容输入流和 bootstrap 初始化链路，运行：

```bash
dotnet run --project openLuo.playgraound -- content-bootstrap
```

这个 demo 不依赖 LLM。它会打印：

- 当前 `ContentRegistry` 中的角色 / 资源 / 物品 / tool / skill / plugin config 数量
- 一个插件 default + character override merge 的实际结果
- 一次 `SessionBootstrapper` 的 materialize 结果
- 持久化后的 `GameState`
- 持久化后的角色记录
- 一个已经写入的资源值示例

这个 demo 的目的就是回答：

- 新 schema 模式现在是否已经开始生效
- 新输入流到底干了什么
- 初始化链路是否已经由 `registry + bootstrap` 接管
