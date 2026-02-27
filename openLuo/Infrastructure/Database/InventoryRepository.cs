using Dapper;
using openLuo.Core.Interfaces;
using openLuo.Infrastructure.ErrorHandling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace openLuo.Infrastructure.Database;

public class InventoryRepository(string connectionString, ILogger<InventoryRepository> logger) : IInventoryRepository
{
    private readonly string _pooledConnectionString =
        new SqliteConnectionStringBuilder(connectionString) { Pooling = true }.ToString();

    public async Task<Dictionary<string, int>> GetAllAsync(string gameId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            var rows = await conn.QueryAsync<(string item_id, int quantity)>(
                "SELECT item_id, quantity FROM inventory WHERE game_id = @gameId AND quantity > 0",
                new { gameId });
            return rows.ToDictionary(r => r.item_id, r => r.quantity);
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"GetAllAsync({gameId})");
            throw;
        }
    }

    public async Task AddItemAsync(string gameId, string itemId, int quantity = 1, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            await conn.ExecuteAsync("""
                INSERT INTO inventory (game_id, item_id, quantity) VALUES (@gameId, @itemId, @quantity)
                ON CONFLICT(game_id, item_id) DO UPDATE SET quantity = quantity + @quantity
                """, new { gameId, itemId, quantity });
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"AddItemAsync({gameId}, {itemId}, {quantity})");
            throw;
        }
    }

    public async Task<bool> RemoveItemAsync(string gameId, string itemId, int quantity = 1, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            var current = await conn.ExecuteScalarAsync<int?>(
                "SELECT quantity FROM inventory WHERE game_id = @gameId AND item_id = @itemId",
                new { gameId, itemId });
            if (current is null || current < quantity) return false;
            await conn.ExecuteAsync(
                "UPDATE inventory SET quantity = quantity - @quantity WHERE game_id = @gameId AND item_id = @itemId",
                new { gameId, itemId, quantity });
            return true;
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"RemoveItemAsync({gameId}, {itemId}, {quantity})");
            throw;
        }
    }
}
