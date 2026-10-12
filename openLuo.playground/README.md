# openLuo.playground（程序集名 `openLuo.Playground`）

最小**可运行** Demo：用脚本化 LLM + 内存实现直接装配内核的「能力决策循环」，离线、确定性，不碰网络与数据库。

```bash
make run-playground     # 或 dotnet run --project openLuo.playground
```

演示链路：`TurnRequest → ComposedAgentRuntime → DefaultCapabilityDecisionLoop`（LLM 原生 tool_calls）
`→ DefaultCapabilityDispatcher → 示例能力 playground:echo → 最终回复`。
组合根就在 `Program.cs` 内（`PlaygroundComposition.Create()`，依赖全是本文件里的 Demo 小实现）。

## 依赖边界

只引用内核与装配层（`openLuo.Application` / `AgentContext` / `Capabilities` + `.Llm` / `Llm`），
**不引用宿主 exe** —— 与扩展同一套边界（见 `docs/architecture/layers.md` §2 B5）。

## 历史说明（2026-10-12 架构检视）

原先的 `Demos/**`（12 个 `.cs` + 2 个 python 演示插件）与 `Infrastructure/**`（3 个 `.cs`）**已删除**：
它们既不参与编译（当时的 `EnableDefaultCompileItems=false` 只编译 `Program.cs`），
引用的内核类型（`CharacterExecNode` / `UnifiedAgentCapabilityRegistry` / `ChatTagRenderer` / `McpPluginHost` …）
又早已从主工程移除，因此**照现状根本编译不过**，属于纯历史参考代码（≈2100 行）。
需要那些场景时，请按当前内核 API 重写为新的 Demo，并保持"不依赖宿主 exe"。
