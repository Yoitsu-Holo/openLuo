using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.Gameplay.Application.Services;
using openLuo.Modules.PluginRuntime.Core.Interfaces;

namespace openLuo.Modules.GameBridge.Infrastructure.Handlers;

public class ShopApiHandler(ShopService shopService, IGameBridgeContextAccessor bridgeContextAccessor)
{
    private static readonly JsonSerializerOptions _serOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [GameApi("game/shop/categories")]
    public async Task<JsonNode?> ListShopCategoriesAsync(string gameId)
    {
        try
        {
            var resolvedGameId = !string.IsNullOrWhiteSpace(gameId) ? gameId : bridgeContextAccessor.Current?.GameId;
            if (string.IsNullOrWhiteSpace(resolvedGameId))
                return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");
            var categories = await shopService.GetCategoriesAsync(resolvedGameId);
            return JsonNode.Parse(JsonSerializer.Serialize(new { categories }, _serOpts));
        }
        catch (InvalidOperationException ex) when (ex.Message == "no_active_game")
        {
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");
        }
    }

    [GameApi("game/shop/list")]
    public async Task<JsonNode?> ListShopItemsAsync(string gameId, string? categoryId = null, int categoryIndex = 0, int page = 1)
    {
        var resolvedGameId = !string.IsNullOrWhiteSpace(gameId) ? gameId : bridgeContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            if (string.IsNullOrWhiteSpace(resolvedGameId))
                return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");
            var categories = await shopService.GetCategoriesAsync(resolvedGameId);
            if (categoryIndex < 1 || categoryIndex > categories.Count)
                return JsonNode.Parse("{\"ok\":false,\"error\":\"missing categoryId\"}");
            categoryId = categories[categoryIndex - 1].CategoryId;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(resolvedGameId))
                return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");
            var view = await shopService.GetPageAsync(resolvedGameId, categoryId, page);
            return JsonNode.Parse(JsonSerializer.Serialize(new
            {
                ok = true,
                view.CategoryId,
                view.CategoryName,
                view.Page,
                view.TotalPages,
                currentGold = view.CurrentGold,
                items = view.Items
            }, _serOpts));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, error = ex.Message }, _serOpts));
        }
    }

    [GameApi("game/shop/buy")]
    public async Task<JsonNode?> BuyShopItemAsync(string gameId, string? categoryId = null, int categoryIndex = 0, int itemIndex = 0, int quantity = 1)
    {
        var resolvedGameId = !string.IsNullOrWhiteSpace(gameId) ? gameId : bridgeContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            if (string.IsNullOrWhiteSpace(resolvedGameId))
                return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");
            var categories = await shopService.GetCategoriesAsync(resolvedGameId);
            if (categoryIndex < 1 || categoryIndex > categories.Count)
                return JsonNode.Parse("{\"ok\":false,\"error\":\"missing categoryId\"}");
            categoryId = categories[categoryIndex - 1].CategoryId;
        }
        if (itemIndex <= 0)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"missing itemIndex\"}");

        try
        {
            if (string.IsNullOrWhiteSpace(resolvedGameId))
                return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");
            var purchase = await shopService.PurchaseAsync(resolvedGameId, categoryId, itemIndex, quantity);
            return JsonNode.Parse(JsonSerializer.Serialize(new
            {
                ok = purchase.Success,
                purchase.ItemId,
                purchase.ItemName,
                description = purchase.ItemDescription,
                purchase.Price,
                purchase.RemainingGold,
                purchase.Error
            }, _serOpts));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, error = ex.Message }, _serOpts));
        }
    }
}
