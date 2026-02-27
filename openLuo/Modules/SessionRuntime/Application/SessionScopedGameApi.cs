using System.Text.Json.Nodes;
using openLuo.Modules.GameBridge.Infrastructure.Handlers;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

/// <summary>
/// Session-scoped wrapper that auto-resolves gameId from SessionHandle
/// and delegates to [GameApi]-annotated handler methods.
///
/// Frontend callers never pass gameId — it's injected transparently.
/// </summary>
public sealed class SessionScopedGameApi(
    SessionHandle handle,
    ISessionRegistry sessionRegistry,
    GameStateApiHandler stateHandler,
    PlayerApiHandler playerHandler,
    ShopApiHandler shopHandler,
    GiftApiHandler giftHandler,
    ResourceApiHandler resourceHandler,
    AssetApiHandler assetHandler,
    LifecycleApiHandler lifecycleHandler) : ISessionGameApi
{
    private string ResolveGameId()
    {
        // Priority: 1. handle.GameId (cached)  2. registry lookup (fresh)
        if (!string.IsNullOrWhiteSpace(handle.GameId))
            return handle.GameId;

        var fresh = sessionRegistry.Get(handle.SessionId)?.GameId;
        if (!string.IsNullOrWhiteSpace(fresh))
            handle.GameId = fresh;
        return fresh ?? throw new InvalidOperationException("Session is not bound to a game. Call InitGameAsync first.");
    }

    // ── State / Time ──────────────────────────────────────────────────────

    public Task<JsonNode?> GetStateAsync(CancellationToken ct) =>
        stateHandler.GetGameStateAsync(ResolveGameId());

    public Task<JsonNode?> UpdateStateAsync(
        string? playerName = null, string? archetypeId = null,
        string? activeCharacterId = null, CancellationToken ct = default) =>
        stateHandler.UpdateGameStateAsync(ResolveGameId(), playerName, archetypeId, activeCharacterId);

    public Task<JsonNode?> GetTimeAsync(CancellationToken ct) =>
        stateHandler.GetGameTimeAsync(ResolveGameId());

    public Task<JsonNode?> AdvanceTimeAsync(int minutes, CancellationToken ct) =>
        stateHandler.AdvanceGameTimeAsync(ResolveGameId(), minutes);

    // ── Character ─────────────────────────────────────────────────────────

    public Task<JsonNode?> GetCharacterAsync(CancellationToken ct) =>
        playerHandler.GetPlayerCharacterAsync(ResolveGameId());

    public Task<JsonNode?> UpdateCharacterAsync(string relationshipStage, CancellationToken ct) =>
        playerHandler.UpdatePlayerCharacterAsync(ResolveGameId(), relationshipStage);

    // ── Inventory ─────────────────────────────────────────────────────────

    public Task<JsonNode?> GetInventoryAsync(CancellationToken ct) =>
        playerHandler.GetPlayerInventoryAsync(ResolveGameId());

    public Task<JsonNode?> AddInventoryAsync(string itemId, int quantity = 1, CancellationToken ct = default) =>
        playerHandler.AddPlayerInventoryAsync(ResolveGameId(), itemId, quantity);

    public Task<JsonNode?> RemoveInventoryAsync(string itemId, int quantity = 1, CancellationToken ct = default) =>
        playerHandler.RemovePlayerInventoryAsync(ResolveGameId(), itemId, quantity);

    public JsonNode? ListItems() => playerHandler.ListAllItems();

    // ── Shop ──────────────────────────────────────────────────────────────

    public Task<JsonNode?> ListShopCategoriesAsync(CancellationToken ct) =>
        shopHandler.ListShopCategoriesAsync(ResolveGameId());

    public Task<JsonNode?> ListShopItemsAsync(
        string? categoryId = null, int categoryIndex = 0, int page = 1, CancellationToken ct = default) =>
        shopHandler.ListShopItemsAsync(ResolveGameId(), categoryId, categoryIndex, page);

    public Task<JsonNode?> BuyShopItemAsync(
        string? categoryId = null, int categoryIndex = 0, int itemIndex = 0,
        int quantity = 1, CancellationToken ct = default) =>
        shopHandler.BuyShopItemAsync(ResolveGameId(), categoryId, categoryIndex, itemIndex, quantity);

    // ── Gift ──────────────────────────────────────────────────────────────

    public Task<JsonNode?> ExecuteGiftAsync(string itemRef, CancellationToken ct) =>
        giftHandler.ExecuteGiftAsync(ResolveGameId(), itemRef);

    // ── Resource ──────────────────────────────────────────────────────────

    public Task<JsonNode?> GetResourceStatusAsync(
        string? characterId = null, bool includeHidden = false,
        bool includePluginItems = true, CancellationToken ct = default) =>
        resourceHandler.GetResourceStatusAsync(ResolveGameId(), characterId, includeHidden, includePluginItems);

    public Task<JsonNode?> QueryResourceValuesAsync(
        string? @namespace = null, string? ownerKind = null, string? ownerId = null,
        string[]? keys = null, bool includeDefaults = false, CancellationToken ct = default) =>
        resourceHandler.QueryResourceValuesAsync(ResolveGameId(), @namespace, ownerKind, ownerId, keys, includeDefaults);

    // ── Asset ─────────────────────────────────────────────────────────────

    public Task<JsonNode?> GetAssetAsync(
        string assetId, bool includeBlobInfo = true, bool includeMeta = true,
        bool includeLinks = false, CancellationToken ct = default) =>
        assetHandler.GetGameAssetAsync(assetId, includeBlobInfo, includeMeta, includeLinks);

    public Task<JsonNode?> QueryAssetsAsync(JsonNode? queryParams = null, CancellationToken ct = default)
    {
        return assetHandler.QueryGameAssetsAsync(
            ResolveGameId(),
            queryParams?["assetType"]?.GetValue<string>(),
            queryParams?["namespace"]?.GetValue<string>(),
            queryParams?["ownerKind"]?.GetValue<string>(),
            queryParams?["ownerId"]?.GetValue<string>(),
            queryParams?["sourceType"]?.GetValue<string>(),
            queryParams?["labelLike"]?.GetValue<string>(),
            queryParams?["linkedTo"]?["entityType"]?.GetValue<string>(),
            queryParams?["linkedTo"]?["entityId"]?.GetValue<string>(),
            queryParams?["limit"]?.GetValue<int>() ?? 50,
            queryParams?["offset"]?.GetValue<int>() ?? 0);
    }

    // ── Diary ─────────────────────────────────────────────────────────────

    public Task<JsonNode?> WriteDiaryAsync(int day, string content, CancellationToken ct = default) =>
        lifecycleHandler.WriteGameDiaryAsync(ResolveGameId(), day, content);

    public Task<JsonNode?> ListDiaryAsync(int offset = 0, int limit = 10, CancellationToken ct = default) =>
        lifecycleHandler.ListGameDiaryAsync(ResolveGameId(), offset, limit);
}
