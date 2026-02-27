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
}

/// <summary>MCP server 集合配置。</summary>
public sealed class McpServersConfig
{
    public IReadOnlyList<McpServerConfig> Servers { get; init; } = [];
}
