using Dapper;
using openLuo.Infrastructure.ErrorHandling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace openLuo.Infrastructure.Database;

public sealed record ShopOfferRecord(string CategoryId, string ItemId, int Price, int SortOrder);
public sealed record ShopCategoryCount(string CategoryId, int Count);

public class ShopOfferRepository(string connectionString, ILogger<ShopOfferRepository> logger)
{
    private readonly string _pooledConnectionString =
        new SqliteConnectionStringBuilder(connectionString) { Pooling = true }.ToString();

    public async Task<int> CountAsync(string gameId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            return await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM shop_offers WHERE game_id = @gameId",
                new { gameId });
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"CountAsync({gameId})");
            throw;
        }
    }

    public async Task<IReadOnlyList<ShopOfferRecord>> ListByCategoryAsync(string gameId, string categoryId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            var rows = await conn.QueryAsync<(string CategoryId, string ItemId, int Price, int SortOrder)>(
                """
                SELECT category_id AS CategoryId, item_id AS ItemId, CAST(price AS INTEGER) AS Price, CAST(sort_order AS INTEGER) AS SortOrder
                FROM shop_offers
                WHERE game_id = @gameId AND category_id = @categoryId
                ORDER BY sort_order, item_id
                """,
                new { gameId, categoryId });
            return rows.Select(row => new ShopOfferRecord(row.CategoryId, row.ItemId, row.Price, row.SortOrder)).ToList();
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"ListByCategoryAsync({gameId}, {categoryId})");
            throw;
        }
    }

    public async Task<IReadOnlyList<ShopCategoryCount>> GetCategoryCountsAsync(string gameId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            var rows = await conn.QueryAsync<(string CategoryId, int Count)>(
                """
                SELECT category_id AS CategoryId, CAST(COUNT(*) AS INTEGER) AS Count
                FROM shop_offers
                WHERE game_id = @gameId
                GROUP BY category_id
                """,
                new { gameId });
            return rows.Select(row => new ShopCategoryCount(row.CategoryId, row.Count)).ToList();
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"GetCategoryCountsAsync({gameId})");
            throw;
        }
    }

    public async Task UpsertBatchAsync(string gameId, IEnumerable<ShopOfferRecord> offers, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            foreach (var offer in offers)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO shop_offers (game_id, category_id, item_id, price, sort_order)
                    VALUES (@gameId, @CategoryId, @ItemId, @Price, @SortOrder)
                    ON CONFLICT(game_id, category_id, item_id)
                    DO UPDATE SET price = excluded.price, sort_order = excluded.sort_order
                    """,
                    new
                    {
                        gameId,
                        offer.CategoryId,
                        offer.ItemId,
                        offer.Price,
                        offer.SortOrder
                    },
                    tx);
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"UpsertBatchAsync({gameId})");
            throw;
        }
    }
}
