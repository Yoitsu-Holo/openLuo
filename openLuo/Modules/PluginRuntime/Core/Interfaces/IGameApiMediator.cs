using System.Text.Json.Nodes;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Modules.PluginRuntime.Core.Interfaces;

/// <summary>
/// Mediator interface for game API requests from plugins.
/// Breaks circular dependency between McpPluginHost and GameApiHandler.
/// </summary>
public interface IGameApiMediator
{
    Task<JsonNode?> HandleAsync(string method, JsonNode? @params, GameBridgeRequestContext? context = null);
}
