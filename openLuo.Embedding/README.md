# openLuo.Embedding

独立子工程：**文本向量生成能力库**。与 LLM 聊天调用（`openLuo.Llm`）解耦，供语义检索等上层能力复用。

## 用途

- 将文本编码为向量（当前基于 `Microsoft.Extensions.AI` + OpenAI-compatible embedding API）
- 按配置动态创建/切换真实 embedding client（支持热重载）

## 结构

```
Core/
  Interfaces/IEmbeddingClient.cs    embedding 接口（只暴露 EmbedAsync / Enabled）
  Models/EmbeddingConfig.cs         配置 POCO：provider / baseUrl / model / 超时 / 重试
Infrastructure/
  RuntimeConfiguredEmbeddingClient.cs   运行时配置代理：Func<EmbeddingConfig> 读取最新配置
  EmbeddingClientFactory.cs             配置 → 具体 client 的分发点
  Clients/MicrosoftAiEmbeddingClient.cs 基于 Microsoft.Extensions.AI/OpenAI 的实现
  Common/EmbeddingProviderRouting.cs    provider 路由决策
```

```text
IEmbeddingClient
├── RuntimeConfiguredEmbeddingClient   // 入口：按当前配置缓存/切换
└── MicrosoftAiEmbeddingClient        // 当前统一实现

EmbeddingClientFactory
  -> provider => MicrosoftAiEmbeddingClient
```

## 配置与注入

`EmbeddingConfig` 定义在本工程，宿主 `AppConfig.Embedding` 复用。运行时代理通过构造函数注入 `Func<EmbeddingConfig>` 读取最新配置（热重载生效）：

```csharp
services.AddSingleton<IEmbeddingClient>(_ => new RuntimeConfiguredEmbeddingClient(() => config.Embedding));
```

## 依赖

- `openLuo.Foundation`（`PromptSanitizer`、静态 `Logger`）
- 包：`Microsoft.Extensions.AI` / `Microsoft.Extensions.AI.OpenAI`
- 无任何宿主模块引用

## 边界

- 只负责向量生成，不负责向量存储/检索（见 `openLuo.Memory`）
- `openLuo.Memory` 等上层模块只依赖 `IEmbeddingClient` 接口，不感知 provider 实现
