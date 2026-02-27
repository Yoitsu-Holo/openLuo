using Dapper;
using openLuo.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace openLuo.Infrastructure.Tests;

/// <summary>
/// Tests for DatabaseInitializer: schema creation, migrations, and idempotency.
/// Uses a temporary file-based SQLite database per test.
/// </summary>
public class DatabaseInitializerTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly string _connStr;

    public DatabaseInitializerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gimai_test_{Guid.NewGuid():N}.db");
        _connStr = $"Data Source={_dbPath}";
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    private async Task<List<string>> GetTableNamesAsync()
    {
        await using var conn = new SqliteConnection(_connStr);
        var names = await conn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' OR type='shadow'");
        return names.ToList();
    }

    // ── Schema creation ────────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_CreatesGameStateTable()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var tables = await GetTableNamesAsync();
        Assert.Contains("game_state", tables);
    }

    [Fact]
    public async Task InitializeAsync_CreatesCharactersTable()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var tables = await GetTableNamesAsync();
        Assert.Contains("characters", tables);
    }

    [Fact]
    public async Task InitializeAsync_CreatesMemoriesTable()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var tables = await GetTableNamesAsync();
        Assert.Contains("memories", tables);
    }

    [Fact]
    public async Task InitializeAsync_CreatesDiariesTable()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var tables = await GetTableNamesAsync();
        Assert.Contains("diaries", tables);
    }

    [Fact]
    public async Task InitializeAsync_CreatesInventoryTable()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var tables = await GetTableNamesAsync();
        Assert.Contains("inventory", tables);
    }

    [Fact]
    public async Task InitializeAsync_CreatesShopOffersTable()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var tables = await GetTableNamesAsync();
        Assert.Contains("shop_offers", tables);
    }

    [Fact]
    public async Task InitializeAsync_CreatesTimelineEventsTable()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var tables = await GetTableNamesAsync();
        Assert.Contains("timeline_events", tables);
    }

    [Fact]
    public async Task InitializeAsync_InventoryHasGameIdColumn()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        await using var conn = new SqliteConnection(_connStr);
        var columns = (await conn.QueryAsync<(int cid, string name, string type, int notnull, string dflt_value, int pk)>(
            "PRAGMA table_info('inventory')")).Select(c => c.name).ToList();
        Assert.Contains("game_id", columns);
    }

    [Fact]
    public async Task InitializeAsync_MigratesLegacyInventoryTable_ToScopedSchema()
    {
        await using (var conn = new SqliteConnection(_connStr))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("""
                CREATE TABLE inventory (
                    item_id TEXT NOT NULL,
                    quantity INTEGER DEFAULT 0,
                    PRIMARY KEY (item_id)
                );
                INSERT INTO inventory (item_id, quantity) VALUES ('flower', 2);
                """);
        }

        await new DatabaseInitializer(_connStr).InitializeAsync();

        await using var verifyConn = new SqliteConnection(_connStr);
        await verifyConn.OpenAsync();
        var columns = (await verifyConn.QueryAsync<(int cid, string name, string type, int notnull, string dflt_value, int pk)>(
            "PRAGMA table_info('inventory')")).Select(c => c.name).ToList();
        Assert.Contains("game_id", columns);

        var quantity = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT quantity FROM inventory WHERE game_id = '' AND item_id = 'flower'");
        Assert.Equal(2, quantity);
    }

    [Fact]
    public async Task InitializeAsync_CreatesAffectionEventsTable()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var tables = await GetTableNamesAsync();
        Assert.Contains("affection_events", tables);
    }

    [Fact]
    public async Task InitializeAsync_CreatesFts5VirtualTable()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var tables = await GetTableNamesAsync();
        Assert.Contains("memories_fts", tables);
    }

    [Fact]
    public async Task InitializeAsync_CreatesVecMemoriesTable()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var tables = await GetTableNamesAsync();
        Assert.Contains("vec_memories", tables);
    }

    [Fact]
    public async Task InitializeAsync_VecMemoriesUsesConfiguredDimension()
    {
        await new DatabaseInitializer(_connStr, sqliteVecDimensions: 1536).InitializeAsync();
        await using var conn = new SqliteConnection(_connStr);
        var sql = await conn.ExecuteScalarAsync<string>(
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='vec_memories'");
        Assert.Contains("float[1536]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("game_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("character_id", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── Idempotency / migration ────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_CalledTwice_DoesNotThrow()
    {
        var init = new DatabaseInitializer(_connStr);
        await init.InitializeAsync();
        // Second call should be idempotent (IF NOT EXISTS + safe ALTER TABLE)
        var ex = await Record.ExceptionAsync(() => init.InitializeAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task InitializeAsync_GameStateHasCurrentMinuteColumn()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        await using var conn = new SqliteConnection(_connStr);
        var cols = await conn.QueryAsync<string>("PRAGMA table_info(game_state)");
        // PRAGMA table_info returns rows; we extract just column names via dynamic
        var colNames = (await conn.QueryAsync<dynamic>("PRAGMA table_info(game_state)"))
            .Select(r => (string)r.name)
            .ToList();
        Assert.Contains("current_minute", colNames);
    }

    [Fact]
    public async Task InitializeAsync_GameStateHasCurrentLocationColumn()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        await using var conn = new SqliteConnection(_connStr);
        var colNames = (await conn.QueryAsync<dynamic>("PRAGMA table_info(game_state)"))
            .Select(r => (string)r.name)
            .ToList();
        Assert.Contains("current_location", colNames);
    }

    [Fact]
    public async Task InitializeAsync_DiariesHasDayAndContentColumns()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        await using var conn = new SqliteConnection(_connStr);
        var colNames = (await conn.QueryAsync<dynamic>("PRAGMA table_info(diaries)"))
            .Select(r => (string)r.name)
            .ToList();
        Assert.Contains("day", colNames);
        Assert.Contains("content", colNames);
        Assert.Contains("created_at", colNames);
    }

    [Fact]
    public async Task InitializeAsync_MemoriesHasGameIdColumn()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        await using var conn = new SqliteConnection(_connStr);
        var colNames = (await conn.QueryAsync<dynamic>("PRAGMA table_info(memories)"))
            .Select(r => (string)r.name)
            .ToList();
        Assert.Contains("game_id", colNames);
    }
}
