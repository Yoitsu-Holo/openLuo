using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Content.Application;
using openLuo.Modules.Content.Core.Definitions;
using openLuo.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Infrastructure.Database;

namespace openLuo.Modules.Gameplay.Application.Services;

public sealed class ShopService(
    IGameStateRepository stateRepo,
    IInventoryRepository inventoryRepository,
    IStateQueryService stateQueryService,
    IStateMutationService stateMutationService,
    ItemDefinitionCatalog itemCatalog,
    IGameContextAccessor gameContextAccessor,
    ShopOfferRepository? shopOfferRepository = null)
{
    public IReadOnlyList<ShopCategorySummary> GetCategories() =>
        GetCategoriesAsync().GetAwaiter().GetResult();

    public async Task<IReadOnlyList<ShopCategorySummary>> GetCategoriesAsync(CancellationToken ct = default)
    {
        if (shopOfferRepository is null)
        {
            return ItemDefinitionCatalog.Categories
                .Select(category => new ShopCategorySummary(
                    category.Category,
                    category.CategoryName,
                    itemCatalog.GetByCategory(category.Category).Count))
                .ToList();
        }

        var state = await ResolveCurrentStateAsync(ct)
            ?? throw new InvalidOperationException("游戏未初始化。");
        return await GetCategoriesAsync(state.Id, ct);
    }

    public async Task<IReadOnlyList<ShopCategorySummary>> GetCategoriesAsync(string gameId, CancellationToken ct = default)
    {
        if (shopOfferRepository is null)
        {
            return ItemDefinitionCatalog.Categories
                .Select(category => new ShopCategorySummary(
                    category.Category,
                    category.CategoryName,
                    itemCatalog.GetByCategory(category.Category).Count))
                .ToList();
        }
        await EnsureDefaultsAsync(gameId, ct);
        var counts = (await shopOfferRepository.GetCategoryCountsAsync(gameId, ct))
            .ToDictionary(x => x.CategoryId, x => x.Count, StringComparer.OrdinalIgnoreCase);

        return ItemDefinitionCatalog.Categories
            .Select(category => new ShopCategorySummary(
                category.Category,
                category.CategoryName,
                counts.TryGetValue(category.Category, out var count) ? count : 0))
            .ToList();
    }

    public async Task<ShopPageResult> GetPageAsync(
        string gameId,
        string categoryId,
        int page,
        int pageSize = 8)
    {
        var category = ItemDefinitionCatalog.Categories.FirstOrDefault(c =>
            c.Category.Equals(categoryId, StringComparison.OrdinalIgnoreCase));
        if (category == default)
            throw new ArgumentOutOfRangeException(nameof(categoryId), $"Unknown shop category: {categoryId}");

        List<(ShopOfferRecord Offer, ItemDefinition Item)> items;
        if (shopOfferRepository is null)
        {
            items = itemCatalog.GetByCategory(category.Category)
                .Select((item, index) => (new ShopOfferRecord(category.Category, item.Id, item.Price, index + 1), item))
                .ToList();
        }
        else
        {
            await EnsureDefaultsAsync(gameId);
            var offers = await shopOfferRepository.ListByCategoryAsync(gameId, category.Category);
            items = offers
                .Select(offer => (Offer: offer, Item: itemCatalog.GetById(offer.ItemId)))
                .Where(x => x.Item is not null)
                .Select(x => (x.Offer, Item: x.Item!))
                .ToList();
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)Math.Max(1, pageSize)));
        var normalizedPage = Math.Clamp(page, 1, totalPages);
        var currentGold = await ReadGoldAsync(gameId);

        var views = items
            .Skip((normalizedPage - 1) * pageSize)
            .Take(pageSize)
            .Select((entry, localIndex) => new ShopItemSummary(
                ItemId: entry.Item.Id,
                Name: entry.Item.DisplayName,
                Description: entry.Item.Description,
                Price: entry.Offer.Price,
                Rarity: entry.Item.Rarity,
                Index: ((normalizedPage - 1) * pageSize) + localIndex + 1))
            .ToList();

        return new ShopPageResult(
            CategoryId: category.Category,
            CategoryName: category.CategoryName,
            Page: normalizedPage,
            TotalPages: totalPages,
            CurrentGold: currentGold,
            Items: views);
    }

    public Task<ShopPageResult> GetListingAsync(
        string gameId,
        int categoryIndex,
        int page,
        int pageSize = 8)
    {
        var category = GetCategoryByIndex(categoryIndex);
        return GetPageAsync(gameId, category.Category, page, pageSize);
    }

    public async Task<ShopPageResult> GetCategoryPageAsync(
        string categoryId,
        int page,
        int pageSize = 8)
    {
        var state = await ResolveCurrentStateAsync()
            ?? throw new InvalidOperationException("游戏未初始化。");
        return await GetPageAsync(state.Id, categoryId, page, pageSize);
    }

    public async Task<ShopPurchaseResult> PurchaseAsync(
        string gameId,
        string categoryId,
        int itemIndex,
        int quantity = 1)
    {
        var category = ItemDefinitionCatalog.Categories.FirstOrDefault(c =>
            c.Category.Equals(categoryId, StringComparison.OrdinalIgnoreCase));
        if (category == default)
            return ShopPurchaseResult.Fail("未知商店分类。");

        ShopOfferRecord offer;
        ItemDefinition? item;
        if (shopOfferRepository is null)
        {
            var items = itemCatalog.GetByCategory(category.Category);
            if (itemIndex < 1 || itemIndex > items.Count)
                return ShopPurchaseResult.Fail($"商品编号无效（1-{items.Count}）。");
            item = items[itemIndex - 1];
            offer = new ShopOfferRecord(category.Category, item.Id, item.Price, itemIndex);
        }
        else
        {
            await EnsureDefaultsAsync(gameId);
            var offers = await shopOfferRepository.ListByCategoryAsync(gameId, category.Category);
            if (itemIndex < 1 || itemIndex > offers.Count)
                return ShopPurchaseResult.Fail($"商品编号无效（1-{offers.Count}）。");
            offer = offers[itemIndex - 1];
            item = itemCatalog.GetById(offer.ItemId);
            if (item is null)
                return ShopPurchaseResult.Fail($"商品定义缺失：{offer.ItemId}");
        }

        var totalCost = offer.Price * Math.Max(1, quantity);
        var gold = await ReadGoldAsync(gameId);
        if (gold < totalCost)
            return ShopPurchaseResult.Fail($"金币不足（需要 {totalCost} G，当前 {gold} G）。");

        var mutation = new StateMutation
        {
            Namespace = "game_resource",
            Key = "gold",
            OwnerKind = StateOwnerKind.Game,
            OwnerId = "global",
            Op = "delta",
            Value = (-totalCost).ToString()
        };
        var results = await stateMutationService.ApplyAsync(gameId, [mutation]);
        var result = results[0];
        var newGold = result.NewValue ?? (gold - totalCost).ToString();
        await inventoryRepository.AddItemAsync(gameId, item.Id, Math.Max(1, quantity));

        return ShopPurchaseResult.Ok(
            item.Id,
            item.DisplayName,
            item.Description,
            offer.Price,
            int.TryParse(newGold, out var remainingGold) ? remainingGold : gold - totalCost);
    }

    public Task<ShopPurchaseResult> PurchaseAsync(
        string gameId,
        int categoryIndex,
        int itemIndex,
        int quantity = 1)
    {
        var category = GetCategoryByIndex(categoryIndex);
        return PurchaseAsync(gameId, category.Category, itemIndex, quantity);
    }

    public async Task<ShopPurchaseResult> BuyAsync(
        string categoryId,
        int itemIndex,
        int quantity = 1)
    {
        var state = await ResolveCurrentStateAsync()
            ?? throw new InvalidOperationException("游戏未初始化。");
        return await PurchaseAsync(state.Id, categoryId, itemIndex, quantity);
    }

    private async Task<GameState?> ResolveCurrentStateAsync(CancellationToken ct = default)
    {
        var gameId = gameContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        return await stateRepo.GetAsync(gameId, ct);
    }

    private async Task<int> ReadGoldAsync(string gameId)
    {
        var stateValue = await stateQueryService.GetAsync(gameId, "game_resource", StateOwnerKind.Game, "global", "gold");
        return int.TryParse(stateValue.Value, out var gold) ? gold : 0;
    }

    private async Task EnsureDefaultsAsync(string gameId, CancellationToken ct = default)
    {
        if (shopOfferRepository is null)
            return;
        if (await shopOfferRepository.CountAsync(gameId, ct) > 0)
            return;

        var offers = new List<ShopOfferRecord>();
        foreach (var category in ItemDefinitionCatalog.Categories)
        {
            var sortOrder = 1;
            foreach (var item in itemCatalog.GetByCategory(category.Category))
            {
                offers.Add(new ShopOfferRecord(category.Category, item.Id, item.Price, sortOrder++));
            }
        }

        await shopOfferRepository.UpsertBatchAsync(gameId, offers, ct);
    }

    private static (string Category, string CategoryName) GetCategoryByIndex(int categoryIndex)
    {
        if (categoryIndex < 1 || categoryIndex > ItemDefinitionCatalog.Categories.Count)
            throw new ArgumentOutOfRangeException(nameof(categoryIndex), $"分类编号无效（1-{ItemDefinitionCatalog.Categories.Count}）。");
        return ItemDefinitionCatalog.Categories[categoryIndex - 1];
    }
}

public sealed record ShopCategorySummary(string CategoryId, string CategoryName, int Count);

public sealed record ShopItemSummary(
    string ItemId,
    string Name,
    string Description,
    int Price,
    string Rarity,
    int Index);

public sealed record ShopPageResult(
    string CategoryId,
    string CategoryName,
    int Page,
    int TotalPages,
    int CurrentGold,
    IReadOnlyList<ShopItemSummary> Items);

public sealed class ShopPurchaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string ItemId { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string ItemDescription { get; init; } = string.Empty;
    public int RemainingGold { get; init; }

    public static ShopPurchaseResult Fail(string error) => new() { Success = false, Error = error };

    public static ShopPurchaseResult Ok(string itemId, string itemName, string itemDescription, int price, int remainingGold) => new()
    {
        Success = true,
        ItemId = itemId,
        ItemName = itemName,
        ItemDescription = itemDescription,
        Price = price,
        RemainingGold = remainingGold
    };

    public int Price { get; init; }
}
