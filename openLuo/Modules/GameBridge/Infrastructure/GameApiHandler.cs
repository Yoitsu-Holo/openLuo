using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;
using openLuo.Modules.GameBridge.Infrastructure.Handlers;

namespace openLuo.Modules.GameBridge.Infrastructure;

/// <summary>
/// Plugin-facing JSON-RPC reverse-proxy. Routes game/* method calls
/// to [GameApi]-annotated handler methods via GameApiDispatcher.
/// </summary>
public class GameApiHandler(
    GameApiDispatcher dispatcher,
    IServiceProvider services,
    LifecycleApiHandler lifecycleHandler,
    HostBridgeApiHandler hostBridgeHandler) : IGameApiMediator
{
    private static readonly JsonSerializerOptions _serOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private IPluginHost? pluginHost;

    public void SetPluginHost(IPluginHost host)
    {
        pluginHost = host;
        lifecycleHandler.SetPluginHost(host);
        hostBridgeHandler.SetPluginHost(host);
    }

    public async Task<JsonNode?> HandleAsync(
        string method, JsonNode? @params, GameBridgeRequestContext? context = null)
    {
        try
        {
            // Inject params.gameId into context as fallback (matching legacy behavior)
            var effectiveContext = context;
            var paramsGameId = @params?["gameId"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(paramsGameId)
                && string.IsNullOrWhiteSpace(effectiveContext?.GameId))
            {
                effectiveContext = new GameBridgeRequestContext
                {
                    GameId = paramsGameId,
                    SessionId = effectiveContext?.SessionId,
                    ClientType = effectiveContext?.ClientType,
                    ClientId = effectiveContext?.ClientId,
                    SourceId = effectiveContext?.SourceId,
                    ChannelId = effectiveContext?.ChannelId,
                    ActorId = effectiveContext?.ActorId,
                    Reason = effectiveContext?.Reason
                };
            }

            var result = await dispatcher.DispatchAsync(
                method, @params, effectiveContext, services, CancellationToken.None);

            // Dispatcher returns object? — convert back to JsonNode?
            return result switch
            {
                null => null,
                JsonNode n => n,
                string s => JsonNode.Parse(s),
                _ => JsonSerializer.SerializeToNode(result, _serOpts)
            };
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return JsonNode.Parse(
                $"{{\"ok\":false,\"error\":\"internal_error\",\"detail\":\"{JsonEncodedText.Encode(ex.Message)}\"}}");
        }
    }
}
