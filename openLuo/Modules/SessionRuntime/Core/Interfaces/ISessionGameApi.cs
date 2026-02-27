using System.Text.Json.Nodes;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

/// <summary>
/// Session-scoped facade over [GameApi] handler methods.
/// Frontend callers (CLI/TUI/QQBot) use this instead of calling handlers directly.
/// gameId is resolved automatically from the session binding — no need to pass it.
///
/// Methods present here = frontend can call. Methods absent = frontend cannot call,
/// even if the underlying handler has a [GameApi] attribute (plugin-only).
/// </summary>
public interface ISessionGameApi
{
    // ── State / Time ──────────────────────────────────────────────────────

    Task<JsonNode?> GetStateAsync(CancellationToken ct = default);
    Task<JsonNode?> UpdateStateAsync(string? playerName = null, string? archetypeId = null, string? activeCharacterId = null, CancellationToken ct = default);
    Task<JsonNode?> GetTimeAsync(CancellationToken ct = default);
    Task<JsonNode?> AdvanceTimeAsync(int minutes, CancellationToken ct = default);

    // ── Character ─────────────────────────────────────────────────────────

    Task<JsonNode?> GetCharacterAsync(CancellationToken ct = default);
    Task<JsonNode?> UpdateCharacterAsync(string relationshipStage, CancellationToken ct = default);

    // ── Inventory ─────────────────────────────────────────────────────────

    Task<JsonNode?> GetInventoryAsync(CancellationToken ct = default);
    Task<JsonNode?> AddInventoryAsync(string itemId, int quantity = 1, CancellationToken ct = default);
    Task<JsonNode?> RemoveInventoryAsync(string itemId, int quantity = 1, CancellationToken ct = default);
    JsonNode? ListItems();

    // ── Shop ──────────────────────────────────────────────────────────────

    Task<JsonNode?> ListShopCategoriesAsync(CancellationToken ct = default);
    Task<JsonNode?> ListShopItemsAsync(string? categoryId = null, int categoryIndex = 0, int page = 1, CancellationToken ct = default);
    Task<JsonNode?> BuyShopItemAsync(string? categoryId = null, int categoryIndex = 0, int itemIndex = 0, int quantity = 1, CancellationToken ct = default);

    // ── Gift ──────────────────────────────────────────────────────────────

    Task<JsonNode?> ExecuteGiftAsync(string itemRef, CancellationToken ct = default);

    // ── Resource ──────────────────────────────────────────────────────────

    Task<JsonNode?> GetResourceStatusAsync(string? characterId = null, bool includeHidden = false, bool includePluginItems = true, CancellationToken ct = default);
    Task<JsonNode?> QueryResourceValuesAsync(string? @namespace = null, string? ownerKind = null, string? ownerId = null, string[]? keys = null, bool includeDefaults = false, CancellationToken ct = default);

    // ── Asset ─────────────────────────────────────────────────────────────

    Task<JsonNode?> GetAssetAsync(string assetId, bool includeBlobInfo = true, bool includeMeta = true, bool includeLinks = false, CancellationToken ct = default);
    Task<JsonNode?> QueryAssetsAsync(JsonNode? queryParams = null, CancellationToken ct = default);

    // ── Diary ─────────────────────────────────────────────────────────────

    Task<JsonNode?> WriteDiaryAsync(int day, string content, CancellationToken ct = default);
    Task<JsonNode?> ListDiaryAsync(int offset = 0, int limit = 10, CancellationToken ct = default);
}
