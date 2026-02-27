using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Application.Models.StateEvaluation;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Models.HookContexts;
using openLuo.Modules.PluginRuntime.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.PluginRuntime.Infrastructure;

public class McpPluginHost : IPluginHost
{
    private readonly List<PluginProcess> _plugins = [];
    private readonly string _logDir;
    private readonly IGameLogger? _logger;
    private readonly IRuntimeConfigCenter? _configCenter;
    private readonly IAgentFlowRegistry? _flowRegistry;
    private readonly ISessionExecutionContextAccessor? _sessionExecutionContextAccessor;
    private readonly IGameBridgeContextAccessor _bridgeContextAccessor;
    private readonly ITimeService? _timeService;
    private readonly IResourceStatusProjectionService? _resourceStatusProjectionService;
    private IGameApiMediator? _apiHandler;
    private readonly List<AgentFlowRegistration> _registeredFlows = [];
    private static readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    public McpPluginHost(
        string baseDir,
        IGameLogger? logger = null,
        IRuntimeConfigCenter? configCenter = null,
        IAgentFlowRegistry? flowRegistry = null,
        ISessionExecutionContextAccessor? sessionExecutionContextAccessor = null,
        IGameBridgeContextAccessor? bridgeContextAccessor = null,
        ITimeService? timeService = null,
        IResourceStatusProjectionService? resourceStatusProjectionService = null)
    {
        _logDir = Path.Combine(baseDir, "logs", "protocol");
        _logger = logger;
        _configCenter = configCenter;
        _flowRegistry = flowRegistry;
        _sessionExecutionContextAccessor = sessionExecutionContextAccessor;
        _bridgeContextAccessor = bridgeContextAccessor ?? new AsyncLocalGameBridgeContextAccessor();
        _timeService = timeService;
        _resourceStatusProjectionService = resourceStatusProjectionService;
        Directory.CreateDirectory(_logDir);
    }

    /// <summary>注入 C# 数据接口，供插件反向调用</summary>
    public void SetApiHandler(IGameApiMediator handler) => _apiHandler = handler;

    public async Task LoadAllAsync(string pluginsDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(pluginsDir)) return;

        foreach (var dir in Directory.EnumerateDirectories(pluginsDir))
        {
            var manifestPath = Path.Combine(dir, "plugin.jsonc");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var manifest = JsonSerializer.Deserialize<PluginManifest>(
                    await File.ReadAllTextAsync(manifestPath, ct), _opts);
                if (manifest is null) continue;
                if (manifest.Disabled)
                {
                    _logger?.Info("plugin", $"skipped disabled plugin {manifest.Id ?? Path.GetFileName(dir)}");
                    continue;
                }

                var entryPath = Path.Combine(dir, manifest.Entry ?? "main.py");
                if (!File.Exists(entryPath)) continue;

                var plugin = new PluginProcess(manifest, entryPath, _logDir, HandleHostRequest, _logger, configCenter: _configCenter);
                await plugin.StartAsync(ct);
                _plugins.Add(plugin);
                RegisterPluginFlows(plugin);
                _logger?.Info("plugin", $"loaded {manifest.Id}", new { version = manifest.Version, commands = plugin.Commands.Count });
            }
            catch (Exception ex)
            {
                _logger?.Error("plugin", $"load failed: {dir}", new { error = ex.Message });
            }
        }

        // Fire onStartup hook so plugins can register resources and initialize
        await CallHookAsync("onStartup", new { }, ct);
    }

    public IReadOnlyList<CommandDescriptor> GetRegisteredCommands() =>
        _plugins.SelectMany(p => p.Commands)
            .Select(command => new CommandDescriptor
            {
                Name = command.Name,
                Aliases = command.Aliases,
                HelpShort = command.HelpShort,
                Category = command.Category,
                Prefix = command.Prefix,
                Usage = command.Usage,
                RiskLevel = command.RiskLevel,
                NeedsConfirm = command.NeedsConfirm,
                Capabilities = command.Capabilities,
                ProviderId = command.PluginId
            })
            .ToList();

    public IReadOnlyList<AgentFlowRegistration> GetRegisteredFlows() => _registeredFlows.ToList();

    public async Task<CommandResult> ExecutePluginCommandAsync(
        string commandName, object args, CancellationToken ct = default, string? category = null, GameBridgeRequestContext? context = null)
    {
        var plugin = _plugins.FirstOrDefault(p =>
            p.Commands.Any(c =>
                (string.IsNullOrWhiteSpace(category) || string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase)) &&
                (c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase)
                 || c.Aliases.Contains(commandName, StringComparer.OrdinalIgnoreCase))));
        if (plugin is null)
            return CommandResult.Fail($"插件指令 /{commandName} 未找到");

        var pluginCmd = plugin.Commands.FirstOrDefault(c =>
            (string.IsNullOrWhiteSpace(category) || string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase)) &&
            (c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase)
             || c.Aliases.Contains(commandName, StringComparer.OrdinalIgnoreCase)));

        // Special handling for chat command - needs longer timeout for LLM calls
        var runtimeConfig = _configCenter?.GetSnapshot().PluginRuntime;
        var timeout = commandName.Equals("chat", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(1, runtimeConfig?.ChatCommandTimeoutSeconds ?? 120)
            : (pluginCmd?.TimeoutSeconds ?? 0) > 0 ? pluginCmd!.TimeoutSeconds : Math.Max(1, runtimeConfig?.DefaultCommandTimeoutSeconds ?? 30);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        JsonNode? raw;
        try
        {
            var bridgeContext = context ?? ResolveBridgeContext();
            raw = await plugin.RawCallAsync("tools/call",
                new { name = commandName, arguments = AttachBridgeContext(args, bridgeContext) }, ct, timeoutSeconds: timeout);
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Fail($"插件指令 /{commandName} 超时（{timeout} 秒）");
        }
        sw.Stop();

        var text = raw?["content"]?[0]?["text"]?.GetValue<string>();
        if (text is null) return CommandResult.Ok("");

        var result = JsonSerializer.Deserialize<PluginCommandResult>(text, _opts);
        var ok = result?.Success != false;
        _logger?.Info("plugin", $"cmd /{commandName} [{(ok ? "ok" : "fail")}] {sw.ElapsedMilliseconds}ms",
            ok ? null : new { error = result?.Error });
        return ok ? CommandResult.Ok(result?.Output ?? text)
                  : CommandResult.Fail(result?.Error ?? "插件指令执行失败");
    }

    public async Task<PluginHookResult> CallHookAsync(
        string hookName, object args, CancellationToken ct = default, GameBridgeRequestContext? context = null)
    {
        var merged = new PluginHookResult();
        var bridgeContext = context ?? ResolveBridgeContext();
        foreach (var plugin in _plugins.Where(p => p.HasHook(hookName) && p.IsAlive))
        {
            try
            {
                var result = await plugin.CallHookAsync(hookName, AttachBridgeContext(args, bridgeContext), ct);
                MergeResult(merged, result);
                _logger?.Debug("plugin", $"hook {hookName} [{plugin.Id}] ok");
            }
            catch (Exception ex)
            {
                _logger?.Error("plugin", $"hook {hookName} [{plugin.Id}] failed", new { error = ex.Message });
            }
        }
        return merged;
    }

    /// <summary>Call onPromptContext hook for all plugins, collect PromptFragments sorted by priority desc.</summary>
    public async Task<List<PromptFragment>> CallPromptContextHookAsync(
        OnPromptContextInput input, CancellationToken ct = default)
    {
        var fragments = new List<PromptFragment>();
        await EnrichHookContextAsync(input, input.CharacterId, ct);
        foreach (var plugin in _plugins.Where(p => p.HasHook("onPromptContext") && p.IsAlive))
        {
            try
            {
                var raw = await RawCallHookWithFallbackAsync(plugin, "onPromptContext", input, ct);
                var items = raw?["promptFragments"]?.AsArray();
                if (items != null)
                    foreach (var item in items)
                        if (item != null)
                            fragments.Add(new PromptFragment(
                                item["phase"]?.GetValue<string>() ?? input.Phase,
                                item["priority"]?.GetValue<int>() ?? 0,
                                item["text"]?.GetValue<string>() ?? ""));
            }
            catch (Exception ex)
            {
                _logger?.Error("plugin", $"hook onPromptContext [{plugin.Id}] failed", new { error = ex.Message });
            }
        }
        return [.. fragments.OrderByDescending(f => f.Priority)];
    }

    /// <summary>Call onStatusQuery hook for all plugins, collect StatusItems.</summary>
    public async Task<List<StatusItem>> CallStatusQueryHookAsync(
        OnStatusQueryInput input, CancellationToken ct = default)
    {
        var items = new List<StatusItem>();
        await EnrichHookContextAsync(input, input.CharacterId, ct);
        foreach (var plugin in _plugins.Where(p => p.HasHook("onStatusQuery") && p.IsAlive))
        {
            try
            {
                var raw = await RawCallHookWithFallbackAsync(plugin, "onStatusQuery", input, ct);
                var statusItems = raw?["statusItems"]?.AsArray();
                if (statusItems != null)
                    foreach (var si in statusItems)
                        if (si != null)
                            items.Add(new StatusItem
                            {
                                Id = si["id"]?.GetValue<string>() ?? "",
                                Label = si["label"]?.GetValue<string>() ?? "",
                                Type = si["type"]?.GetValue<string>() ?? "bar",
                                Value = si["value"]?.ToString() ?? "0",
                                Max = si["max"]?.ToString(),
                                Group = si["group"]?.GetValue<string>() ?? "stat",
                                Order = si["order"]?.GetValue<int>() ?? 100,
                                Text = si["text"]?.GetValue<string>() ?? ""
                            });
            }
            catch (Exception ex)
            {
                _logger?.Error("plugin", $"hook onStatusQuery [{plugin.Id}] failed", new { error = ex.Message });
            }
        }
        return items;
    }

    public async Task<OnChatAfterOutput> CallChatAfterHookAsync(
        OnChatAfterInput input, CancellationToken ct = default)
    {
        var merged = new OnChatAfterOutput();
        await EnrichHookContextAsync(input, input.CharacterId, ct);

        foreach (var plugin in _plugins.Where(p => p.HasHook("onChatAfter") && p.IsAlive))
        {
            try
            {
                var raw = await RawCallHookWithFallbackAsync(plugin, "onChatAfter", input, ct);
                if (raw is null)
                    continue;

                var parsed = raw.Deserialize<OnChatAfterOutput>(_opts);
                if (parsed?.AdditionalText is not null)
                    merged.AdditionalText = string.Concat(merged.AdditionalText ?? string.Empty, parsed.AdditionalText);
                if (parsed?.Notices is { Count: > 0 })
                    merged.Notices = [.. (merged.Notices ?? []).Concat(parsed.Notices.Where(x => !string.IsNullOrWhiteSpace(x)))];
            }
            catch (Exception ex)
            {
                _logger?.Error("plugin", $"hook onChatAfter [{plugin.Id}] failed", new { error = ex.Message });
            }
        }

        return merged;
    }

    public async Task<OnToolExecutedOutput> CallToolExecutedHookAsync(
        OnToolExecutedInput input, CancellationToken ct = default)
    {
        var merged = new OnToolExecutedOutput();
        await EnrichHookContextAsync(input, input.CharacterId, ct);

        foreach (var plugin in _plugins.Where(p => p.HasHook("onToolExecuted") && p.IsAlive))
        {
            try
            {
                var raw = await RawCallHookWithFallbackAsync(plugin, "onToolExecuted", input, ct);
                if (raw is null)
                    continue;

                var parsed = raw.Deserialize<OnToolExecutedOutput>(_opts);
                if (parsed?.AdditionalText is not null)
                    merged.AdditionalText = string.Concat(merged.AdditionalText ?? string.Empty, parsed.AdditionalText);
                if (parsed?.Notices is { Count: > 0 })
                    merged.Notices = [.. (merged.Notices ?? []).Concat(parsed.Notices.Where(x => !string.IsNullOrWhiteSpace(x)))];
            }
            catch (Exception ex)
            {
                _logger?.Error("plugin", $"hook onToolExecuted [{plugin.Id}] failed", new { error = ex.Message });
            }
        }

        return merged;
    }

    // 处理插件发来的反向请求（game/* 数据接口）
    private async Task<JsonNode?> HandleHostRequest(string method, JsonNode? @params)
    {
        if (_apiHandler is null) return null;
        var previous = _bridgeContextAccessor.Current;
        var context = ResolveBridgeContext();
        _bridgeContextAccessor.Current = context;
        try
        {
            return await _apiHandler.HandleAsync(method, @params, context);
        }
        finally
        {
            _bridgeContextAccessor.Current = previous;
        }
    }

    private GameBridgeRequestContext? ResolveBridgeContext()
    {
        var current = _bridgeContextAccessor.Current;
        if (current is not null)
            return current;

        var sessionContext = _sessionExecutionContextAccessor?.Current;
        if (sessionContext is null)
            return null;

        return new GameBridgeRequestContext
        {
            SessionId = sessionContext.SessionId,
            GameId = sessionContext.GameId,
            ClientType = sessionContext.Origin is null ? null : sessionContext.Origin.ToString(),
            SourceId = sessionContext.SourceId,
            ChannelId = sessionContext.ChannelId,
            ActorId = sessionContext.ActorId
        };
    }

    private async Task<JsonNode?> RawCallHookWithFallbackAsync(
        PluginProcess plugin,
        string hookName,
        object args,
        CancellationToken ct)
    {
        var raw = await plugin.RawCallAsync("hooks/call", new { hookName, args }, ct);
        if (raw is not null)
            return raw;

        return await plugin.RawCallAsync("tools/call", new { name = hookName, arguments = args }, ct);
    }

    private async Task EnrichHookContextAsync(HookContext context, string? characterId, CancellationToken ct)
    {
        var bridgeContext = ResolveBridgeContext();
        context.GameId ??= bridgeContext?.GameId;
        context.SessionId ??= bridgeContext?.SessionId;
        context.SourceId ??= bridgeContext?.SourceId;
        context.ChannelId ??= bridgeContext?.ChannelId;
        context.ActorId ??= bridgeContext?.ActorId;
        context.Reason ??= bridgeContext?.Reason;
        context.BridgeContext ??= bridgeContext;
        context.PresentationProfile ??= _sessionExecutionContextAccessor?.Current?.PresentationProfile.ToString();

        if (_timeService is not null && !string.IsNullOrWhiteSpace(context.GameId))
        {
            var time = await _timeService.GetSnapshotAsync(context.GameId, ct);
            if (time is not null)
                ApplyTimeSnapshot(context, time);
        }

        if (_resourceStatusProjectionService is not null &&
            context is IResourceAwareHookContext resourceAware &&
            !string.IsNullOrWhiteSpace(context.GameId) &&
            !string.IsNullOrWhiteSpace(characterId))
        {
            var snapshot = await _resourceStatusProjectionService.BuildStatusSnapshotAsync(new ResourceStatusQuery
            {
                GameId = context.GameId,
                CharacterId = characterId,
                ArchetypeId = context.ArchetypeId,
                IncludeHidden = true,
                IncludePluginItems = false
            }, ct);
            resourceAware.ResourceSnapshot = snapshot.Items;
        }
    }

    private static void ApplyTimeSnapshot(HookContext context, TimeSnapshot snapshot)
    {
        var time = new HookTimeSnapshot
        {
            Day = snapshot.Day,
            Minute = snapshot.Minute,
            TimeStr = snapshot.TimeStr,
            Mode = snapshot.Mode.ToString().ToLowerInvariant(),
            EpochMs = snapshot.EpochMs
        };

        switch (context)
        {
            case OnPromptContextInput prompt:
                prompt.TimeSnapshot = time;
                break;
            case OnStatusQueryInput status:
                status.TimeSnapshot = time;
                break;
            case OnChatAfterInput after:
                after.TimeSnapshot = time;
                break;
        }
    }

    private static string NormalizePresentationProfile(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Default" : value.Trim();

    private static object AttachBridgeContext(object args, GameBridgeRequestContext? context)
    {
        if (context is null)
            return args;

        var node = JsonSerializer.SerializeToNode(args) as JsonObject ?? new JsonObject();
        node["bridgeContext"] = JsonSerializer.SerializeToNode(context);
        return node;
    }

    private static void MergeResult(PluginHookResult merged, PluginHookResult r)
    {
        if (r.AdditionalText is not null) merged.AdditionalText = (merged.AdditionalText ?? "") + r.AdditionalText;
        if (r.SystemPrompt is not null) merged.SystemPrompt = (merged.SystemPrompt ?? "") + r.SystemPrompt;
        if (r.MemoryToStore is not null) merged.MemoryToStore = r.MemoryToStore;
        if (r.AffectionDelta.HasValue) merged.AffectionDelta = (merged.AffectionDelta ?? 0) + r.AffectionDelta.Value;
        if (r.AffectionMultiplier.HasValue) merged.AffectionMultiplier = r.AffectionMultiplier;
        if (r.StaminaBonus.HasValue) merged.StaminaBonus = (merged.StaminaBonus ?? 0) + r.StaminaBonus.Value;
        if (r.DreamText is not null) merged.DreamText = r.DreamText;
        if (r.ModifiedPrompt is not null) merged.ModifiedPrompt = r.ModifiedPrompt;
        if (r.Cancel) merged.Cancel = true;
    }

    private void RegisterPluginFlows(PluginProcess plugin)
    {
        if (plugin.Flows.Count == 0)
            return;

        foreach (var flow in plugin.Flows)
        {
            try
            {
                _flowRegistry?.Register(flow);
                _registeredFlows.Add(flow);
                _logger?.Info("plugin", $"registered flow {flow.Id}", new
                {
                    plugin = plugin.Id,
                    startNodeId = flow.StartNodeId,
                    nodes = flow.Nodes.Count,
                    edges = flow.Edges.Count
                });
            }
            catch (Exception ex)
            {
                _logger?.Error("plugin", $"flow registration failed: {flow.Id}", new
                {
                    plugin = plugin.Id,
                    error = ex.Message
                });
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var p in _plugins)
            await p.DisposeAsync();
    }
}

internal class PluginCommandResult
{
    public bool Success { get; set; } = true;
    public string? Output { get; set; }
    public string? Error { get; set; }
}
