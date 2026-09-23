# openLuo.Llm

独立子工程：**LLM 聊天调用能力库**。负责聊天补全 / 流式输出的 provider 适配与运行时路由，可脱离 openLuo 宿主独立使用。

## 用途

- 统一封装 OpenAI-compatible 协议与 Ollama 原生协议
- 按配置动态路由（多 route 按能力/优先级选择）
- 内置限流、超时、重试、消息清洗（`PromptSanitizer`）与多模态（图像块）载荷

## 结构

```
Core/
  Interfaces/ILlmClient.cs        本地聊天接口（不暴露 Microsoft.Extensions.AI）
  Models/                         消息与选项模型
    ChatMessage / LlmOptions / LlmToolCall / LlmToolSpec / LlmChatResponse
    LlmConfigModels.cs            配置 POCO：LlmProvider / LlmConfig / LlmRouteConfig / LlmCapabilitiesConfig
Infrastructure/Chat/
  LlmClientBase.cs                抽象基类：配置、选项合并、限流、超时、重试、消息清洗
  Adapters/OpenAiCompatibleLlmClient.cs    OpenAI-compatible 协议公共实现
  Providers/DeepSeekLlmClient.cs / QwenLlmClient.cs / OllamaLlmClient.cs
  LlmClientFactory.cs             provider → 具体客户端 的唯一分发点
  LlmRouteSelector.cs             按请求需求（多模态/JSON/tools/streaming）与 route 能力匹配
  RuntimeConfiguredLlmClient.cs   运行时配置代理：按当前配置缓存真实 client（支持热重载）
```

类层次：

```text
ILlmClient
├── RuntimeConfiguredLlmClient        // 入口：Func<LlmConfig> 读取最新配置，缓存/切换真实 client
└── LlmClientBase                     // 共享抽象：选项、限流、超时、重试、清洗
    ├── OpenAiCompatibleLlmClient     // OpenAI-compatible 协议层
    │   ├── DeepSeekLlmClient
    │   └── QwenLlmClient
    └── OllamaLlmClient               // Ollama 原生 /api/chat

LlmClientFactory
  -> provider=DeepSeek => DeepSeekLlmClient
  -> provider=Qwen     => QwenLlmClient
  -> provider=Ollama   => OllamaLlmClient
```

## 配置

配置 POCO（`LlmConfig` / `LlmRouteConfig` / `LlmCapabilitiesConfig` / `LlmProvider`）定义在本工程 `Core/Models`，宿主 `AppConfig.Llm` 直接复用这些类型（宿主 → 本工程引用，方向正确）。

`RuntimeConfiguredLlmClient` **不依赖宿主配置中心**，通过构造函数注入 `Func<LlmConfig>` 读取最新配置：

```csharp
// 宿主 DI 注册示例（热重载仍生效：lambda 每次读取最新快照）
services.AddSingleton<ILlmClient>(_ => new RuntimeConfiguredLlmClient(() => config.Llm));
```

## 依赖

- `openLuo.Foundation`（`Block` 多模态载荷、`PromptSanitizer`、静态 `Logger`）
- 无任何宿主模块引用

## 边界

- 不包含聊天以外的能力（embedding 见 `openLuo.Embedding`）
- provider 特定逻辑下沉到具体客户端，不堆在统一入口
- 对上层只暴露 `openLuo.Modules.Llm.*` 命名空间（与拆分前一致，调用方零改动）

## 复用

其他工程引用 `openLuo.Llm.csproj` 后，注入 `ILlmClient`（或直接构造 `RuntimeConfiguredLlmClient` + `Func<LlmConfig>`）即可使用；`LlmClientFactory.Create(route)` 可用于无路由场景直接构造单客户端。
