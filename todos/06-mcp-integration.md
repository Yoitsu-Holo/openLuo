# 06 · MCP 集成（openLuo.Capabilities.Mcp）

## 1. 定位

MCP 是"远程工具能力"来源，不是领域扩展（D34/D48）。

- 用官方 `ModelContextProtocol` 2.1.0 SDK（D34）
- 宿主级配置 `config/mcp-servers.jsonc`（D48）
- `openLuo.Capabilities.Mcp` 负责连接/发现/调用
- PluginRuntime 整体废弃重写（D34）

## 2. 配置（D48）

```jsonc
// config/mcp-servers.jsonc
{
  "servers": [
    {
      "id": "image-server",
      "transport": "stdio",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-everything"]
    },
    {
      "id": "web-server",
      "transport": "http",
      "url": "http://localhost:3001/mcp"
    }
  ]
}
```

第一版支持 transport：

- `stdio`（本地子进程）
- `http` / `streamable-http`（远程）

## 3. 适配层结构

```csharp
public sealed class McpCapabilitySource : ICapabilitySource, IAsyncDisposable
{
    // 启动时连接所有配置的 server
    // ListCapabilities() → 把 McpClientTool 转换为 CapabilityDescriptor
    // InvokeAsync() → CallToolAsync + 结果转换
}
```

```csharp
public static class McpCapabilitySourceExtensions
{
    public static IServiceCollection AddMcpCapabilities(
        this IServiceCollection services,
        McpServersConfig config);
}
```

## 4. 转换映射

### McpClientTool → CapabilityDescriptor

```csharp
CanonicalId    = $"mcp:{serverId}:{tool.Name}"
Kind           = CapabilityKind.Mcp
ProviderId     = $"mcp:{serverId}"
InputSchema    = tool 的 AIFunction 参数 schema（原生 JSON Schema）
Summary        = tool.Description（截断）
Usage          = tool.Description（何时使用提示）
SideEffect     = 默认 External（MCP 工具视为外部副作用，D10）
Completion     = Continue（默认；工具不直接结束）
Visibility     = Silent（默认；除非工具结果含媒体/明确可展示）
ParallelSafe   = true（默认；可用 AccessesResources 约束）
Idempotency    = Unknown（默认；由 server 能力决定）
```

### CallToolResult → CapabilityResult

```csharp
Status    = IsError ? Failed : Ok
Text      = 拼接 TextContentBlock
Outputs   = Image/Audio/EmbeddedResource → OutputItem（Replyable 时）
Error     = IsError 时取首个文本
```

## 5. core 发现能力（D33）

- `core:list_mcp_servers`：列出已连接 server（id/transport/状态）
- `core:list_mcp_tools`：列出某 server 的 tools（canonical id/摘要）
- 不做 resources/prompts（第一版，与 D33 一致）

## 6. 生命周期

- 宿主启动：读配置 → 连接所有 server → 注册为 ICapabilitySource
- 连接失败：标记 server 不可用（结构化状态），不阻塞宿主启动；可用性进入每轮快照
- 运行期：tools/list 与 tools/call 走官方 SDK
- 关停：DisposeAsync 断开全部连接

## 7. 幂等映射（D11）

- MCP 协议本身无幂等键规范
- 适配层在调用参数/元数据中附加 `idempotency_key`（若 server 支持）
- 不支持的 server：`IdempotencyKind = Unknown`，重试返回风险提示

## 8. 错误与降级

- 工具错误（IsError）→ 结果回填给 Agent，非协议异常（官方 SDK 语义）
- 连接/超时/断线 → 结构化失败，Server 状态标记，Agent 可继续其他能力
- 不受信 server：工具描述视为外部输入；输出剥离标记防提示注入（沿用 PromptSanitizer 安全规则）
