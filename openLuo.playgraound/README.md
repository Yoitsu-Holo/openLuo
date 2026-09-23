# openLuo.playgraound

最小 demo 工程。

用途：
- 单独验证 `openLuo` 内部模块
- 用最小方式演示模块调用
- 避免把实验代码写回主工程

## 当前 demo

### LLM 模块
- `Demos/Llm/LlmClientBaseDemo.cs`（`llm`）
  - 真实 LLM 路由：system + enhance 上下文块 → `ILlmClient.CompleteAsync` → 纯文本回复
- `Demos/Llm/MultimodalImageDemo.cs`（`multimodal <image>`）
  - 多模态图片识别：50×50 纯色块验证管线 → 多分辨率图片 → 逐档测试 payload 上限

### Agent 模块
- `Demos/Agent/ToolCallLoopDemo.cs`（`tool-loop`）
  - **exec 原生 tool_calls 循环的模块级 E2E（离线、确定性，推荐先跑这个）**
  - 链路：`McpPluginHost(演示插件) -> UnifiedAgentCapabilityRegistry(ToToolSpec 工具目录) -> CharacterExecNode(LLM 原生 tool_calls 循环) -> CharacterToolGateway -> UnifiedAgentCapabilityExecutor -> 插件执行 -> tool 消息回填(含媒体块) -> CharacterResponseNode -> 最终回复`
  - LLM 用脚本化假客户端：第一轮请求 `demo_generate_image`（真实插件返回内嵌图片），第二轮请求 `character_response`
- `Demos/Agent/ContextSystemDemo.cs`（`context-system`）
  - 上下文系统：`CharacterTurnContext -> AgentContextManager -> AgentExecutorContextCompiler`
  - 验证 todo_list / char_resp 上下文投影、历史图片保留、exec 循环 `ToolMessages` 通道（工具文本并入 ToolResults、媒体块并入对话、`RequestVision` 门控）
- `Demos/Agent/EnhanceChatDemo.cs`（`enhance-chat`）
  - **消息级增强（EnhanceChat）最小 E2E**
  - 链路：`turn.Metadata（存储层，永不进正文）-> ChatTagRenderer 白名单渲染 -> message.Tags -> LLM 序列化拼接`
  - 验证：Content 原文纯净、未知 metadata key 不进入 LLM、时间标记与扩展标记按序拼接
- `Demos/Agent/SubgraphFlowDemo.cs`
  - 单独验证：`flow.subgraph` 调用另一个已注册子图
- `Demos/Agent/TurnMessageEmitterDemo.cs`
  - 单独验证：节点产出的 `TurnMessage -> OutputEventBus -> MessageOutput / TurnCompleted`

### Executor 模块
- `Demos/Executor/MemoryRecallExecutorDemo.cs`（`memory-recall`）
- `Demos/Executor/MemoryKeywordFallbackDemo.cs`（`memory-fallback`）
- `Demos/Executor/MemoryVectorDemo.cs`（`memory-vector`）
- `Demos/Executor/FlowRoutingExecutorDemo.cs`（`flow-routing`）

### 其它
- `Demos/Content/ContentBootstrapDemo.cs`（`content-bootstrap`）
  - 单独验证：`raw content -> ContentRegistry -> PluginConfigMerge -> SessionBootstrapper -> persisted state`
- `Demos/Plugin/ToolExecutedHookDemo.cs`（`tool-hook`）
  - 单独验证：`host -> McpPluginHost -> onToolExecuted -> demo plugin`

## 配置

```bash
cp openLuo.playgraound/config/llm.demo.example.ini openLuo.playgraound/config/llm.demo.ini
cp openLuo.playgraound/config/embedding.demo.example.ini openLuo.playgraound/config/embedding.demo.ini
```

`llm.demo.ini` 示例默认切到 Ollama：

- `provider = Ollama`
- `baseUrl = http://localhost:11434`
- `apiKey` 可留空
- `model = qwen3:8b`

`llm` / `multimodal` / memory 系列 demo 走真实 LLM 路由；`tool-loop` 使用脚本化假客户端，无需配置、离线可跑。

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

## 运行

```bash
dotnet run --project openLuo.playgraound
# 默认 = llm

dotnet run --project openLuo.playgraound -- tool-loop
dotnet run --project openLuo.playgraound -- enhance-chat
dotnet run --project openLuo.playgraound -- context-system
dotnet run --project openLuo.playgraound -- multimodal path/to/image.jpg
dotnet run --project openLuo.playgraound -- memory-recall
dotnet run --project openLuo.playgraound -- memory-fallback
dotnet run --project openLuo.playgraound -- memory-vector
dotnet run --project openLuo.playgraound -- flow-routing
dotnet run --project openLuo.playgraound -- content-bootstrap
dotnet run --project openLuo.playgraound -- tool-hook
```

## 说明

- 入口 `Program.cs` 会初始化静态 `Logger`（控制台输出），各模块的日志直接可见
- 演示插件位于 `Demos/Agent/demo_plugins/demo_capability_tool/`（`demo_generate_image` 工具，返回内嵌 1×1 PNG）与 `Demos/Plugin/demo_plugins/tool_executed_probe/`（`onToolExecuted` 钩子）
- goal_executor / tool_use executor 已随原生 tool_calls 改造删除：工具调度由 `CharacterExecNode` 的 LLM 原生 function calling 循环承担，见 `tool-loop` demo
