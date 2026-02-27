using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.Gameplay.Application.Services;
using openLuo.Modules.PluginRuntime.Core.Interfaces;

namespace openLuo.Modules.GameBridge.Infrastructure.Handlers;

public class GiftApiHandler(GiftService giftService, IGameBridgeContextAccessor bridgeContextAccessor)
{
    private static readonly JsonSerializerOptions _serOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [GameApi("game/gift/execute")]
    public async Task<JsonNode?> ExecuteGiftAsync(string gameId, string itemRef)
    {
        if (string.IsNullOrWhiteSpace(itemRef))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"missing itemRef\"}");

        var resolvedGameId = !string.IsNullOrWhiteSpace(gameId) ? gameId : bridgeContextAccessor.Current?.GameId;
        var result = string.IsNullOrWhiteSpace(resolvedGameId)
            ? GiftExecutionView.Fail("no_active_game")
            : await giftService.ExecuteAsync(resolvedGameId, itemRef);
        if (!result.Success)
        {
            return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, error = result.Error }, _serOpts));
        }

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok = true,
            result.ItemId,
            result.ItemName,
            result.CharacterName,
            result.Reply,
            result.AffectionDelta,
            affection = result.CurrentAffection,
            result.RelationshipStage
        }, _serOpts));
    }
}
