using System.Text.Json.Serialization;

namespace openLuo.Capabilities.Mcp;

/// <summary>MCP server 连接配置（D48，宿主级 config/mcp-servers.jsonc）。</summary>
public sealed class McpServerConfig
{
    public string Id { get; init; } = string.Empty;
    public string Transport { get; init; } = "stdio";   // stdio | http | streamable-http
    public string? Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public string? Url { get; init; }
    /// <summary>调用时注入调用方上下文键（_openluo_game_id/session_id/turn_id）到工具参数，供服务器隔离/审计。</summary>
    public bool InjectContextKeys { get; init; } = true;
    /// <summary>附加请求头（鉴权等）。支持 {env:VAR} 占位符从环境变量取值，避免密钥落盘。</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    /// <summary>stdio 子进程环境变量（jsonc 键名 env；如给本地 python server 注入 API key）。
    /// 空则不注入，子进程继承宿主进程环境。注意：jsonc 内为明文，敏感值优先用宿主进程环境变量。</summary>
    [JsonPropertyName("env")]
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();
}

/// <summary>MCP server 集合配置。</summary>
public sealed class McpServersConfig
{
    public IReadOnlyList<McpServerConfig> Servers { get; init; } = [];
}
