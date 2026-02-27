# Llm Module

负责聊天模型调用与 provider 适配。

包含：
- `Core/Interfaces`：`ILlmClient`
- `Core/Models`：`ChatMessage`、`EnhanceMessage`、`SystemMessage`、`LlmOptions`
- `Infrastructure/Chat`：聊天入口层
- `Infrastructure/Chat/Base`：共享抽象基类
- `Infrastructure/Chat/Adapters`：协议族公共实现
- `Infrastructure/Chat/Providers`：各 provider 的具体重载/扩展

## Chat 结构

当前 `chat` 侧已经拆成“共享基类 + provider-specific 客户端”，避免把所有 provider 特判继续堆进一个类里。

继承/分发关系：

```text
ILlmClient
├── RuntimeConfiguredLlmClient      // 运行时配置代理：按当前配置缓存/切换真实 client
└── MicrosoftAiLlmClient            // 基础抽象：所有模型共享的配置、选项、限流、超时、重试、消息清洗
    ├── OpenAiCompatibleLlmClient   // 协议适配层：OpenAI-compatible 公共实现
    │   ├── OpenAiLlmClient         // provider：OpenAI
    │   ├── DeepSeekLlmClient       // provider：DeepSeek
    │   └── QwenLlmClient           // provider：Qwen，附加 Qwen 特有请求体字段
    └── OllamaLlmClient             // provider：Ollama 原生 /api/chat

LlmClientFactory
  -> provider=OpenAI   => OpenAiLlmClient
  -> provider=DeepSeek => DeepSeekLlmClient
  -> provider=Qwen     => QwenLlmClient
  -> provider=Ollama   => OllamaLlmClient
```

职责划分：
- `MicrosoftAiLlmClient`：抽象基类，不是 provider。只放所有模型共享的基础能力，不放 provider-specific 协议细节
- `OpenAiCompatibleLlmClient`：协议适配层，也不是 provider。承接所有 OpenAI-compatible provider 的公共协议
- `OpenAiLlmClient` / `DeepSeekLlmClient` / `QwenLlmClient` / `OllamaLlmClient`：真正的 provider 实现层，负责对基类或协议层做重载与扩展
- `RuntimeConfiguredLlmClient`：根据 `IRuntimeConfigCenter` 代理到当前真实 client
- `LlmClientFactory`：唯一真实分发点，只做 provider 到具体实现的选择

推荐把分层理解为：
- 入口层：`RuntimeConfiguredLlmClient`、`LlmClientFactory`
- 基础层：`MicrosoftAiLlmClient`
- 协议层：`OpenAiCompatibleLlmClient`
- Provider 层：`OpenAiLlmClient`、`DeepSeekLlmClient`、`QwenLlmClient`、`OllamaLlmClient`

边界：
- 不直接依赖玩法逻辑
- 作为 `Agent`、叙事与状态评估的基础能力层
- 对外只暴露 `openLuo.Modules.Llm.*` 命名空间，不再通过全局 `Core` 暴露模块专属契约

当前策略：
- 对上层继续保留本地接口 `ILlmClient`，避免业务模块直接耦合 `Microsoft.Extensions.AI`
- OpenAI / DeepSeek / Qwen 走 OpenAI-compatible 协议
- Ollama 单独走原生 `/api/chat`
- `LlmOptions` 以标准字段为主，必要时允许 `ExtraBody` 透传 provider-specific 扩展字段
- provider-specific 逻辑下沉到具体客户端，不再堆在统一入口类里
- Embedding 已拆分到独立模块 `openLuo/Modules/Embedding`
- 不再保留独立的“解释性路由对象”，避免说明和真实代码分裂

## 配置与模型约束

### LlmConfig.Provider

`LlmConfig.Provider` 现在是枚举，不再使用运行期字符串硬匹配。

当前值：
- `OpenAI`
- `DeepSeek`
- `Qwen`
- `Ollama`

说明：
- 配置文件边界仍然可以写字符串，例如 `"provider": "Qwen"`
- 反序列化阶段会把字符串映射回枚举
- provider 分发逻辑只处理枚举，避免业务层继续散落字符串判断

### ChatMessage.Role

`ChatMessage.Role` 现在也是固定枚举，而不是自由字符串。

当前值：
- `System`
- `User`
- `Assistant`
- `Tool`

约束原因：
- 主流 chat protocol 的 role 本身就是有限集合
- 自定义 role 往往最终仍要在适配层被映射回标准 role，继续暴露自由字符串只会制造假扩展性
- 因此这里明确收口为协议内角色，把“扩展语义”放到消息内容或上层 tool 协议里处理

建议：
- 纯提示词上下文放 `System`
- 用户输入放 `User`
- 模型输出放 `Assistant`
- 工具执行结果放 `Tool`

### EnhanceMessage / SystemMessage

`EnhanceMessage` 用于把结构化增强上下文包装成标准 chat message：

- 输入：`Role + EnhanceMessageRule + Content`
- 输出：`[RULE] ... [/RULE]` 格式的 `ChatMessage`

适用场景：
- `EnhanceMessageRule.CharacterProfile`
- `EnhanceMessageRule.WorldContext`
- `EnhanceMessageRule.SceneState`
- `EnhanceMessageRule.PlayerInput`

`SystemMessage` 是 `ChatMessage(System, content)` 的语法收口：

- 只接收 `Content`
- 默认角色恒为 `system`
- 适合用来承载通用 system prompt，而不再重复显式传 `ChatMessageRole.System`

建议分工：
- 全局规则、协议约束、最高优先级行为边界：`SystemMessage`
- 角色设定、世界观、场景状态、玩家输入等结构化增强块：`EnhanceMessage`
- 普通对话消息：`ChatMessage`

## Provider 兼容性约束

### 不要假设所有 OpenAI-compatible provider 完全等价

- `baseUrl + apiKey + model` 兼容 OpenAI 协议，不代表同时支持：
- 原生结构化输出 / `response_format`
- function calling 的同等语义
- streaming 的相同行为
- 相同的错误格式与重试特征
- 相同的 provider-specific 扩展字段

补充：
- `Qwen` 虽然走 OpenAI-compatible 入口，但仍可能要求额外请求体字段
- `Ollama` 不应再强行视为普通 OpenAI-compatible provider；当前实现明确走原生 `/api/chat`

### 能力分级

- 结构化输出应视为“可选能力”，不是所有 provider 的硬前提。
- 当 provider 不支持原生结构化返回时，应自动回退到：
- 普通文本 completion
- 手动 JSON 提取与解析
- 必要时增加更严格的提示词约束

### 降级优先级

- 业务能力不能因为某个 provider 不支持 `response_format` 就整体失效。
- 例如礼物意图识别、状态评估这类 hook，应优先保证“有降级可用”，而不是把业务逻辑绑定死在结构化接口能力上。

### 代理与网络

- provider 访问能力受运行环境代理配置影响。
- 对本地代理、环境变量代理、直连三种路径都应视为外部环境条件，不应把它们和业务正确性混为一谈。
- 日志必须区分：
- provider/协议不支持
- 代理不可达
- 认证失败
- 业务解析失败

### 错误语义

- LLM 层要尽量把“能力不支持”和“模型返回空/坏 JSON”区分记录。
- 上层业务不应把所有错误都等同看作“模型失败”，而应根据错误类别决定是否降级、重试或跳过。
