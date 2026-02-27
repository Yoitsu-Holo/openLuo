# Embedding Module

负责文本向量生成能力，不再和 `Llm` 聊天模块混放。

包含：
- `Core/Interfaces`：`IEmbeddingClient`
- `Infrastructure`：embedding 入口、运行时代理、工厂
- `Infrastructure/Clients`：具体 embedding provider 实现
- `Infrastructure/Common`：embedding provider 路由说明

结构：

```text
IEmbeddingClient
├── RuntimeConfiguredEmbeddingClient   // 运行时配置代理
└── MicrosoftAiEmbeddingClient         // 当前统一的 embedding 实现

EmbeddingClientFactory
  -> provider string => MicrosoftAiEmbeddingClient
```

职责：
- `RuntimeConfiguredEmbeddingClient`：按当前配置切换真实 embedding client
- `EmbeddingClientFactory`：根据 embedding 配置决定实例化策略
- `MicrosoftAiEmbeddingClient`：基于 `Microsoft.Extensions.AI/OpenAI` 生成向量
- `EmbeddingProviderRouting`：只记录 provider 到适配策略的说明

边界：
- `Embedding` 与 `Llm` 聊天调用解耦
- `Memory` 等上层模块只依赖 `IEmbeddingClient`
- 后续如果要扩展独立的 embedding provider，可直接在该模块内演进
