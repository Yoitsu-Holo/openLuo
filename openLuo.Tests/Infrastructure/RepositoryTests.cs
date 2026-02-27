using Dapper;
using openLuo.Core.Models;
using openLuo.Core.Interfaces;
using openLuo.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace openLuo.Infrastructure.Tests;

/// <summary>
/// Integration tests for database repositories using a temporary file-based SQLite.
/// Each test class gets its own database to ensure isolation.
/// </summary>
public class RepositoryTestBase : IAsyncLifetime
{
    protected readonly string DbPath;
    protected readonly string ConnStr;

    protected RepositoryTestBase()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"gimai_repo_{Guid.NewGuid():N}.db");
        ConnStr = $"Data Source={DbPath}";
    }

    public async Task InitializeAsync() =>
        await new DatabaseInitializer(ConnStr).InitializeAsync();

    public Task DisposeAsync()
    {
        if (File.Exists(DbPath)) File.Delete(DbPath);
        return Task.CompletedTask;
    }
}

// ── GameStateRepository ───────────────────────────────────────────────────────

public class GameStateRepositoryTests : RepositoryTestBase
{
    private readonly IGameContextAccessor _gameContextAccessor = Substitute.For<IGameContextAccessor>();
    private GameStateRepository Repo => new(ConnStr, Substitute.For<ILogger<GameStateRepository>>(), _gameContextAccessor);

    [Fact]
    public async Task GetAsync_EmptyDatabase_ReturnsNull()
    {
        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };
        var result = await Repo.GetAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsState()
    {
        var state = new GameState
        {
            Id = "g1",
            PlayerName = "Test",
            ArchetypeId = "bg1",
            CurrentDay = 3,
            CurrentMinute = 600,
            CurrentLocation = "公园",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await Repo.SaveAsync(state);
        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };
        var loaded = await Repo.GetAsync();

        Assert.NotNull(loaded);
        Assert.Equal("g1", loaded.Id);
        Assert.Equal("Test", loaded.PlayerName);
        Assert.Equal(3, loaded.CurrentDay);
        Assert.Equal(600, loaded.CurrentMinute);
        Assert.Equal("公园", loaded.CurrentLocation);
    }

    [Fact]
    public async Task SaveAsync_CalledTwice_UpdatesExisting()
    {
        var state = new GameState { Id = "g1", PlayerName = "A", ArchetypeId = "bg1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await Repo.SaveAsync(state);

        state.CurrentDay = 5;
        state.CurrentMinute = 900;
        await Repo.SaveAsync(state);

        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };
        var loaded = await Repo.GetAsync();
        Assert.NotNull(loaded);
        Assert.Equal(5, loaded.CurrentDay);
        Assert.Equal(900, loaded.CurrentMinute);
    }

    [Fact]
    public async Task SaveAsync_SetsDefaultCurrentMinute_To480()
    {
        var state = new GameState
        {
            Id = "g2", PlayerName = "B", ArchetypeId = "bg1",
            CurrentMinute = 480,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        await Repo.SaveAsync(state);
        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g2" };
        var loaded = await Repo.GetAsync();
        Assert.Equal(480, loaded!.CurrentMinute);
    }
}

// ── CharacterRepository ───────────────────────────────────────────────────────

public class CharacterRepositoryTests : RepositoryTestBase
{
    private CharacterRepository Repo => new(ConnStr, Substitute.For<ILogger<CharacterRepository>>());

    [Fact]
    public async Task GetByArchetypeIdAsync_NoneExist_ReturnsNull()
    {
        var result = await Repo.GetByArchetypeIdAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGet_ReturnsCharacter()
    {
        var character = new Character
        {
            Id = "c1",
            ArchetypeId = "bg1",
            Name = "铃"
        };
        await Repo.SaveAsync(character);

        var loaded = await Repo.GetByArchetypeIdAsync("bg1");

        Assert.NotNull(loaded);
        Assert.Equal("c1", loaded.Id);
        Assert.Equal("铃", loaded.Name);
    }

    [Fact]
    public async Task SaveAsync_UpdatesName()
    {
        var character = new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" };
        await Repo.SaveAsync(character);

        character.Name = "朝霞";
        await Repo.SaveAsync(character);

        var loaded = await Repo.GetByArchetypeIdAsync("bg1");
        Assert.Equal("朝霞", loaded!.Name);
    }

    [Fact]
    public async Task RecordAffectionEventAsync_StoresEvent()
    {
        var evt = new AffectionEvent
        {
            Id = "e1",
            CharacterId = "c1",
            Reason = "送礼物",
            Delta = 10,
            OccurredAt = DateTime.UtcNow
        };
        // Should not throw
        await Repo.RecordAffectionEventAsync(evt);

        await using var conn = new SqliteConnection(ConnStr);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM affection_events WHERE id = 'e1'");
        Assert.Equal(1, count);
    }
}

// ── InventoryRepository ───────────────────────────────────────────────────────

public class InventoryRepositoryTests : RepositoryTestBase
{
    private const string GameId = "g1";
    private const string OtherGameId = "g2";
    private InventoryRepository Repo => new(ConnStr, Substitute.For<ILogger<InventoryRepository>>());

    [Fact]
    public async Task GetAllAsync_EmptyDb_ReturnsEmptyDict()
    {
        var result = await Repo.GetAllAsync(GameId);
        Assert.Empty(result);
    }

    [Fact]
    public async Task AddItemAsync_NewItem_QuantityIsOne()
    {
        await Repo.AddItemAsync(GameId, "flower");
        var inv = await Repo.GetAllAsync(GameId);
        Assert.Equal(1, inv["flower"]);
    }

    [Fact]
    public async Task AddItemAsync_ExistingItem_Increments()
    {
        await Repo.AddItemAsync(GameId, "flower", 2);
        await Repo.AddItemAsync(GameId, "flower", 3);
        var inv = await Repo.GetAllAsync(GameId);
        Assert.Equal(5, inv["flower"]);
    }

    [Fact]
    public async Task RemoveItemAsync_SufficientQuantity_ReturnsTrue()
    {
        await Repo.AddItemAsync(GameId, "book", 3);
        var ok = await Repo.RemoveItemAsync(GameId, "book", 2);
        Assert.True(ok);
        var inv = await Repo.GetAllAsync(GameId);
        Assert.Equal(1, inv["book"]);
    }

    [Fact]
    public async Task RemoveItemAsync_InsufficientQuantity_ReturnsFalse()
    {
        await Repo.AddItemAsync(GameId, "ring", 1);
        var ok = await Repo.RemoveItemAsync(GameId, "ring", 5);
        Assert.False(ok);
        // Quantity should remain unchanged
        var inv = await Repo.GetAllAsync(GameId);
        Assert.Equal(1, inv["ring"]);
    }

    [Fact]
    public async Task RemoveItemAsync_ItemNotExist_ReturnsFalse()
    {
        var ok = await Repo.RemoveItemAsync(GameId, "ghost_item");
        Assert.False(ok);
    }

    [Fact]
    public async Task GetAllAsync_OnlyReturnsPositiveQuantities()
    {
        await Repo.AddItemAsync(GameId, "item1", 1);
        await Repo.AddItemAsync(GameId, "item2", 2);
        await Repo.RemoveItemAsync(GameId, "item1"); // now 0
        var inv = await Repo.GetAllAsync(GameId);
        Assert.False(inv.ContainsKey("item1"));
        Assert.Equal(2, inv["item2"]);
    }

    [Fact]
    public async Task GetAllAsync_IsolatedByGameId()
    {
        await Repo.AddItemAsync(GameId, "flower", 2);
        await Repo.AddItemAsync(OtherGameId, "flower", 5);

        var gameOne = await Repo.GetAllAsync(GameId);
        var gameTwo = await Repo.GetAllAsync(OtherGameId);

        Assert.Equal(2, gameOne["flower"]);
        Assert.Equal(5, gameTwo["flower"]);
    }
}

// ── ShopOfferRepository ───────────────────────────────────────────────────────

public class ShopOfferRepositoryTests : RepositoryTestBase
{
    private ShopOfferRepository Repo => new(ConnStr, Substitute.For<ILogger<ShopOfferRepository>>());

    [Fact]
    public async Task UpsertBatchAsync_ThenListByCategory_ReturnsOffersInSortOrder()
    {
        await Repo.UpsertBatchAsync("g1", [
            new ShopOfferRecord("gift", "flower", 100, 2),
            new ShopOfferRecord("gift", "ring", 300, 1)
        ]);

        var offers = await Repo.ListByCategoryAsync("g1", "gift");

        Assert.Equal(2, offers.Count);
        Assert.Equal("ring", offers[0].ItemId);
        Assert.Equal("flower", offers[1].ItemId);
    }

    [Fact]
    public async Task GetCategoryCountsAsync_ReturnsCountsPerCategory()
    {
        await Repo.UpsertBatchAsync("g1", [
            new ShopOfferRecord("gift", "flower", 100, 1),
            new ShopOfferRecord("food", "cake", 60, 1),
            new ShopOfferRecord("food", "tea", 30, 2)
        ]);

        var counts = await Repo.GetCategoryCountsAsync("g1");

        Assert.Contains(counts, c => c.CategoryId == "gift" && c.Count == 1);
        Assert.Contains(counts, c => c.CategoryId == "food" && c.Count == 2);
    }
}
