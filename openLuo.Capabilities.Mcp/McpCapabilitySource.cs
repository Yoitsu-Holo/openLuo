using ModelContextProtocol;
using ModelContextProtocol.Client;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Mcp;

/// <summary>
/// MCP 能力来源（D34/D48）：连接配置的 MCP server，把 McpClientTool 转换为 CapabilityDescriptor。
/// 启动时连接；连接失败标记 server 不可用（结构化状态），不阻塞宿主启动。
/// </summary>
public sealed class McpCapabilitySource : ICapabilitySource, IAsyncDisposable
{
    private readonly McpServerConfig _config;
    private IReadOnlyList<McpClientTool> _tools = [];
    private McpClient? _client;

    public McpCapabilitySource(McpServerConfig config)
    {
        _config = config;
    }

    public string ProviderId => $"mcp:{_config.Id}";
    public bool IsHealthy => _client is not null;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        McpClient? client = null;
        try
        {
            client = await CreateClientAsync(ct);
            var tools = (await client.ListToolsAsync(new RequestOptions(), ct)).ToList();
            _client = client;
            _tools = tools;
            client = null;
        }
        catch
        {
            _client = null;
            _tools = [];
            if (client is not null)
                await client.DisposeAsync();
        }
    }

    /// <summary>重建客户端与工具快照（会话过期/服务重启后调用）。失败时保持不可用状态。</summary>
    public async Task<bool> ReconnectAsync(CancellationToken ct = default)
    {
        var old = _client;
        _client = null;
        _tools = [];
        if (old is not null)
            await old.DisposeAsync();
        try
        {
            var client = await CreateClientAsync(ct);
            var tools = (await client.ListToolsAsync(new RequestOptions(), ct)).ToList();
            _client = client;
            _tools = tools;
            return true;
        }
        catch
        {
            _client = null;
            _tools = [];
            return false;
        }
    }

    private async Task<McpClient> CreateClientAsync(CancellationToken ct)
    {
        switch (_config.Transport?.ToLowerInvariant())
        {
            case "stdio":
            {
                if (string.IsNullOrWhiteSpace(_config.Command))
                    throw new InvalidOperationException($"MCP server '{_config.Id}': stdio transport requires 'command'.");
                var transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = _config.Id,
                    Command = _config.Command,
                    Arguments = [.. _config.Args]
                });
                return await McpClient.CreateAsync(transport, cancellationToken: ct);
            }
            case "http":
            case "streamable-http":
            case "streamablehttp":
            {
                if (string.IsNullOrWhiteSpace(_config.Url))
                    throw new InvalidOperationException($"MCP server '{_config.Id}': http transport requires 'url'.");
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(_config.Url),
                    AdditionalHeaders = ExpandHeaders(_config.Headers)
                });
                return await McpClient.CreateAsync(transport, cancellationToken: ct);
            }
            default:
                throw new InvalidOperationException($"MCP server '{_config.Id}': unsupported transport '{_config.Transport}'.");
        }
    }
    internal static Dictionary<string, string> ExpandHeaders(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0)
            return [];
        var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in headers)
            result[key] = ExpandEnv(value);
        return result;
    }

    private static readonly System.Text.RegularExpressions.Regex EnvPlaceholder =
        new(@"\{env:([A-Za-z_][A-Za-z0-9_]*)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string ExpandEnv(string value)
    {
        // 支持内嵌 {env:VAR} 占位符（如 "Bearer {env:MCD_API_KEY}"）；未设置的变量保留原样（连接失败警告会暴露）。
        return EnvPlaceholder.Replace(value, m =>
        {
            var resolved = Environment.GetEnvironmentVariable(m.Groups[1].Value);
            return string.IsNullOrEmpty(resolved) ? m.Value : resolved;
        });
    }

    public ICapabilityInvoker CreateInvoker()
    {
        if (_client is null)
            throw new InvalidOperationException($"MCP server '{_config.Id}' is not connected.");
        return new McpCapabilityInvoker(_client, _config.Id, _config.InjectContextKeys, ReconnectAsync);
    }

    public IReadOnlyList<CapabilityDescriptor> ListCapabilities()
    {
        if (!IsHealthy)
            return [];

        return _tools.Select(tool => new CapabilityDescriptor
        {
            CanonicalId = $"mcp:{_config.Id}:{tool.Name}",
            Kind = CapabilityKind.Mcp,
            ProviderId = ProviderId,
            DisplayName = tool.Name,
            Summary = tool.Description ?? string.Empty,
            Usage = tool.Description ?? string.Empty,
            SideEffect = SideEffectClass.External,
            Completion = CompletionPolicy.Continue,
            Visibility = OutputVisibility.Silent,
            ParallelSafe = true,
            Idempotency = IdempotencyKind.Unknown,
            Version = "1.0.0",
            InputSchema = tool.JsonSchema
        }).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _client = null;
        _tools = [];
    }
}
