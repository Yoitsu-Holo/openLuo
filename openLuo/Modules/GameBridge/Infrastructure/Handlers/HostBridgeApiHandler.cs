using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Modules.GameBridge.Infrastructure.Handlers;

public class HostBridgeApiHandler(
    IGameStreams streams,
    IGameLogger logger,
    IMultiCharacterCommandCatalog? multiCharacterCommandCatalog = null,
    IRuntimeConfigCenter? configCenter = null)
{
    private readonly IGameStreams _streams = streams;
    private readonly IGameLogger _logger = logger;
    private static readonly JsonSerializerOptions _serOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private IPluginHost? pluginHost;

    public void SetPluginHost(IPluginHost host) => pluginHost = host;

    [GameApi("game/commands/list")]
    public JsonNode? ListAllCommands()
    {
        var pluginCommands = pluginHost?.GetRegisteredCommands() ?? [];
        var multiCharacterCommands = multiCharacterCommandCatalog?.GetCommands() ?? [];

        var merged = pluginCommands
            .Concat(multiCharacterCommands)
            .GroupBy(c => $"{c.Prefix}:{c.Name}".ToLowerInvariant())
            .Select(g => g.Last())
            .OrderBy(c => c.Prefix)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var list = merged.Select(c => new
        {
            c.Name,
            c.Aliases,
            c.HelpShort,
            PluginId = c.ProviderId,
            c.Category,
            c.Prefix,
            c.Usage,
            c.RiskLevel,
            c.NeedsConfirm,
            c.Capabilities
        });
        return JsonNode.Parse(JsonSerializer.Serialize(list, _serOpts));
    }

    [GameApi("game/commands/confirm")]
    public async Task<JsonNode?> CommandsConfirmAsync(string name, string[]? args = null, string? hint = null, int? timeoutSeconds = null)
    {
        if (Console.IsInputRedirected)
            return JsonNode.Parse("{\"confirmed\":false,\"timedOut\":false}");

        var argsVal = args ?? [];
        var hintVal = hint ?? name;
        var timeoutVal = timeoutSeconds ?? Math.Max(5, configCenter?.GetSnapshot().Agent.PendingAbilityConfirmTimeoutSeconds ?? 45);
        var argsStr = argsVal.Length > 0 ? " " + string.Join(" ", argsVal) : "";
        var promptBytes = Encoding.UTF8.GetBytes($"\n\uD83D\uDCA1 建议执行：/{name}{argsStr}（{hintVal}）\n是否执行？[Y/n] ");
        await _streams.Output.WriteAsync(promptBytes);
        await _streams.Output.FlushAsync();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutVal));
        try
        {
            var buffer = new byte[1024];
            var read = await _streams.Input.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            var input = Encoding.UTF8.GetString(buffer, 0, read).TrimEnd('\n', '\r');
            input = input.Trim().ToLower();
            var confirmed = input == "" || input == "y" || input == "yes";
            return JsonNode.Parse($"{{\"confirmed\":{(confirmed ? "true" : "false")},\"timedOut\":false}}");
        }
        catch (OperationCanceledException)
        {
            var timeoutBytes = Encoding.UTF8.GetBytes("\n（等待超时，角色继续对话）");
            await _streams.Output.WriteAsync(timeoutBytes);
            await _streams.Output.FlushAsync();
            return JsonNode.Parse("{\"confirmed\":false,\"timedOut\":true}");
        }
    }

    [GameApi("game/commands/execute")]
    public async Task<JsonNode?> CommandsExecuteAsync(string name, string[]? args = null, string? sessionId = null, string? gameId = null, string? clientType = null, string? clientId = null, string? sourceId = null, string? channelId = null, string? actorId = null)
    {
        if (pluginHost is null) return JsonNode.Parse("{\"ok\":false,\"output\":\"\"}");
        var argsVal = args ?? [];
        GameBridgeRequestContext? bridgeContext = null;
        if (!string.IsNullOrWhiteSpace(sessionId) ||
            !string.IsNullOrWhiteSpace(gameId) ||
            !string.IsNullOrWhiteSpace(clientType) ||
            !string.IsNullOrWhiteSpace(clientId) ||
            !string.IsNullOrWhiteSpace(sourceId) ||
            !string.IsNullOrWhiteSpace(channelId) ||
            !string.IsNullOrWhiteSpace(actorId))
        {
            bridgeContext = new GameBridgeRequestContext
            {
                SessionId = sessionId,
                GameId = gameId,
                ClientType = clientType,
                ClientId = clientId,
                SourceId = sourceId,
                ChannelId = channelId,
                ActorId = actorId,
                Reason = "commands/execute"
            };
        }
        var result = await pluginHost.ExecutePluginCommandAsync(name, new { args = argsVal }, context: bridgeContext);
        return JsonNode.Parse(JsonSerializer.Serialize(
            new { ok = result.Success, output = result.Success ? result.Output : result.Error ?? result.Output }, _serOpts));
    }

    [GameApi("game/log")]
    public JsonNode? WritePluginLog(string pluginId, string level, string msg)
    {
        _logger.Plugin(pluginId, level, msg);
        return JsonNode.Parse("{\"ok\":true}");
    }

    [GameApi("game/ui/read_input")]
    public async Task<JsonNode?> ReadUserInputAsync(string? prompt = null)
    {
        if (_streams.Output == null || _streams.Input == null)
            return JsonNode.Parse("{\"input\":\"\"}");

        var promptVal = prompt ?? ">>> ";
        var promptBytes = Encoding.UTF8.GetBytes(promptVal);
        await _streams.Output.WriteAsync(promptBytes);
        await _streams.Output.FlushAsync();
        var buffer = new byte[1024];
        var read = await _streams.Input.ReadAsync(buffer);
        var line = Encoding.UTF8.GetString(buffer, 0, read).TrimEnd('\n', '\r');
        return JsonNode.Parse(JsonSerializer.Serialize(new { input = line }, _serOpts));
    }
}
