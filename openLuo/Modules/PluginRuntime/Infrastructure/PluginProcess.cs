using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.PluginRuntime.Core.Interfaces;

namespace openLuo.Modules.PluginRuntime.Infrastructure;

internal class PluginProcess : IAsyncDisposable
{
    private readonly PluginManifest _manifest;
    private readonly string _logPath;
    private readonly string _pluginId;
    private readonly IGameLogger? _logger;
    private readonly IGameStreams? _streams;
    private Process? _process;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _nextId = 1;
    private readonly HashSet<string> _hooks;
    private readonly Func<string, JsonNode?, Task<JsonNode?>> _hostRequestHandler;
    private readonly IRuntimeConfigCenter? _configCenter;
    private static readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    public string Id => _manifest.Id ?? "unknown";
    public bool IsAlive => _process is { HasExited: false };
    public IReadOnlyList<PluginCommandDef> Commands { get; private set; } = [];
    public IReadOnlyList<AgentFlowRegistration> Flows { get; }

    public PluginProcess(PluginManifest manifest, string entryPath, string logDir,
        Func<string, JsonNode?, Task<JsonNode?>> hostRequestHandler, IGameLogger? logger = null, IGameStreams? streams = null, IRuntimeConfigCenter? configCenter = null)
    {
        _manifest = manifest;
        _pluginId = manifest.Id ?? "unknown";
        _logger = logger;
        _streams = streams;
        _logPath = Path.Combine(logDir, $"{manifest.Id?.Replace("/", "-") ?? "plugin"}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        _hooks = [.. manifest.Hooks ?? []];
        _hostRequestHandler = hostRequestHandler;
        _configCenter = configCenter;
        Flows = manifest.Flows ?? [];

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{entryPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new System.Text.UTF8Encoding(false),
                StandardOutputEncoding = new System.Text.UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(entryPath)
            }
        };
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _process!.Start();
        _process.BeginErrorReadLine();
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _logger?.Debug("plugin", $"[{_pluginId}] {e.Data}");
        };

        await SendAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { tools = new { } },
            clientInfo = new { name = "openLuo", version = "1.0.0" }
        }, ct, timeoutSeconds: Math.Max(1, _configCenter?.GetSnapshot().PluginRuntime.RpcHandshakeTimeoutSeconds ?? 15));

        var toolsResult = await SendAsync("tools/list", new { }, ct, timeoutSeconds: Math.Max(1, _configCenter?.GetSnapshot().PluginRuntime.RpcHandshakeTimeoutSeconds ?? 15));
        Commands = ParseCommands(toolsResult);
    }

    private IReadOnlyList<PluginCommandDef> ParseCommands(JsonNode? toolsResult)
    {
        var tools = toolsResult?["tools"]?.AsArray();
        if (tools is null) return [];

        var cmds = new List<PluginCommandDef>();
        foreach (var tool in tools)
        {
            var category = (tool?["category"]?.GetValue<string>() ?? "command").Trim();

            var name = tool?["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name)) continue;

            var aliases = tool?["aliases"]?.AsArray()
                .Select(a => a?.GetValue<string>() ?? "")
                .Where(a => !string.IsNullOrEmpty(a))
                .ToArray() ?? [];

            cmds.Add(new PluginCommandDef
            {
                Name = name,
                Aliases = aliases,
                HelpShort = tool?["description"]?.GetValue<string>() ?? "",
                Category = category,
                Prefix = ResolvePrefixByCategory(category),
                Usage = tool?["usage"]?.GetValue<string>() ?? "",
                RiskLevel = tool?["riskLevel"]?.GetValue<string>() ?? "low",
                NeedsConfirm = tool?["needsConfirm"]?.GetValue<bool>() ?? false,
                Capabilities = tool?["capabilities"]?.AsArray()?
                    .Select(x => x?.GetValue<string>() ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray() ?? [],
                PluginId = Id,
                TimeoutSeconds = tool?["timeoutSeconds"]?.GetValue<int>() ?? 0,
            });
        }
        return cmds;
    }

    private static string ResolvePrefixByCategory(string category)
    {
        var normalized = category.Trim().ToLowerInvariant();
        return normalized switch
        {
            "tool" => "@",
            "skill" => "$",
            "subagent" => "&",
            "sub_agent" => "&",
            "sub-agent" => "&",
            "agent" => "&",
            _ => "/"
        };
    }

    public bool HasHook(string hookName) => _hooks.Contains(hookName);

    public async Task<PluginHookResult> CallHookAsync(string hookName, object args, CancellationToken ct)
    {
        // Hooks may trigger LLM calls internally (e.g. diary generation), so use a long timeout
        var raw = await RawCallAsync("tools/call", new { name = hookName, arguments = args }, ct, timeoutSeconds: Math.Max(1, _configCenter?.GetSnapshot().PluginRuntime.HookCallTimeoutSeconds ?? 120));
        var content = raw?["content"]?[0]?["text"]?.GetValue<string>();
        if (content is null) return new PluginHookResult();

        return JsonSerializer.Deserialize<PluginHookResult>(content, _opts) ?? new PluginHookResult();
    }

    public async Task<JsonNode?> RawCallAsync(string method, object @params, CancellationToken ct, int timeoutSeconds = 5) =>
        await SendAsync(method, @params, ct, timeoutSeconds);

    private async Task<JsonNode?> SendAsync(string method, object @params, CancellationToken ct, int timeoutSeconds = 5)
    {
        if (_process is null || _process.HasExited) return null;

        var id = _nextId++;
        var request = new { jsonrpc = "2.0", id, method, @params };
        var line = JsonSerializer.Serialize(request);

        await _lock.WaitAsync(ct);
        try
        {
            Log("send", line);
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            while (true)
            {
                var response = await _process.StandardOutput.ReadLineAsync(cts.Token);
                if (response is null) return null;
                Log("recv", response);

                var node = JsonNode.Parse(response);
                if (node is null) continue;

                // Error response with null/missing id — plugin threw an unhandled exception
                if (node["error"] is not null)
                {
                    var nodeId2 = node["id"];
                    if (nodeId2 is null || nodeId2.GetValueKind() == System.Text.Json.JsonValueKind.Null)
                        return null; // surface as failure rather than hanging
                }

                // 匹配响应：id 为整数且与请求 id 一致
                var nodeId = node["id"];
                if (nodeId is not null && nodeId.GetValueKind() == System.Text.Json.JsonValueKind.Number
                    && nodeId.GetValue<int>() == id)
                    return node["result"];

                // 插件发来的反向请求（有 method 字段）
                var incomingMethod = node["method"]?.GetValue<string>();
                if (incomingMethod is not null)
                {
                    var hasId = nodeId is not null && nodeId.GetValueKind() != System.Text.Json.JsonValueKind.Null;
                    if (hasId)
                        await HandleIncomingRequestAsync(node, incomingMethod, ct);
                    else
                        HandleNotification(incomingMethod, node["params"]);
                    continue;
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void HandleNotification(string method, JsonNode? @params)
    {
        if (method == "stream/output")
        {
            var text = @params?["text"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(text))
            {
                if (_streams is not null)
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    _streams.Output.Write(bytes);
                }
                else
                {
                    Console.Write(text);
                }
            }
        }
    }

    private async Task HandleIncomingRequestAsync(JsonNode node, string method, CancellationToken ct)
    {
        var reqId = node["id"];
        try
        {
            // LLM-related methods need longer timeout
            var runtimeConfig = _configCenter?.GetSnapshot().PluginRuntime;
            var timeoutSeconds = method switch
            {
                _ => Math.Max(1, runtimeConfig?.HostRequestDefaultTimeoutSeconds ?? 30)
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var result = await _hostRequestHandler(method, node["params"]).WaitAsync(cts.Token);
            var resp = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = reqId, result });
            Log("send", resp);
            await _process!.StandardInput.WriteLineAsync(resp.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            var resp = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id = reqId,
                error = new { code = -32603, message = ex.Message }
            });
            await _process!.StandardInput.WriteLineAsync(resp.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);
        }
    }

    private void Log(string dir, string msg)
    {
        try
        {
            var entry = JsonSerializer.Serialize(new { ts = DateTime.UtcNow, dir, msg });
            File.AppendAllText(_logPath, entry + "\n");
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try { _process.StandardInput.Close(); } catch { }
            await Task.Delay(Math.Max(1, _configCenter?.GetSnapshot().PluginRuntime.ProcessShutdownGraceMs ?? 500));
            if (!_process.HasExited) _process.Kill();
        }
        _process?.Dispose();
    }
}

internal class PluginManifest
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Entry { get; set; }
    public bool Disabled { get; set; }
    public string[]? Hooks { get; set; }
    public string[]? Permissions { get; set; }

    /// <summary>Plugin type (e.g. "state", "asset", "narrative").</summary>
    public string? PluginType { get; set; }

    /// <summary>Plugin priority for prompt fragment ordering.</summary>
    public int Priority { get; set; } = 0;

    /// <summary>State IDs declared by state plugins.</summary>
    public string[]? States { get; set; }

    /// <summary>State IDs declared by state plugins (alternate key).</summary>
    public string[]? ProvidesStates { get; set; }

    /// <summary>Legacy state IDs declared by older manifests.</summary>
    public string[]? Resources { get; set; }

    /// <summary>Legacy state IDs declared by older manifests (alternate key).</summary>
    public string[]? ProvidesResources { get; set; }

    /// <summary>Flow registrations declared by the plugin manifest.</summary>
    public AgentFlowRegistration[]? Flows { get; set; }
}
