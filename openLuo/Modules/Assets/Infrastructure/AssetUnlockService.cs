using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;
using Microsoft.Data.Sqlite;

namespace openLuo.Modules.Assets.Infrastructure;

public class AssetUnlockService(string connectionString) : IAssetUnlockService
{
    public async Task<string> UnlockAsync(
        string gameId,
        string ownerKind,
        string ownerId,
        string entityType,
        string entityId,
        string unlockType,
        string? metadataJson = null)
    {
        var unlockId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("O");

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO unlock_records
                (id, game_id, owner_kind, owner_id, entity_type, entity_id, unlock_type, unlocked_at, metadata_json)
            VALUES
                (@id, @g, @ok, @oi, @et, @ei, @ut, @ua, @mj)
            """;
        cmd.Parameters.AddWithValue("@id", unlockId);
        cmd.Parameters.AddWithValue("@g", gameId);
        cmd.Parameters.AddWithValue("@ok", ownerKind);
        cmd.Parameters.AddWithValue("@oi", ownerId);
        cmd.Parameters.AddWithValue("@et", entityType);
        cmd.Parameters.AddWithValue("@ei", entityId);
        cmd.Parameters.AddWithValue("@ut", unlockType);
        cmd.Parameters.AddWithValue("@ua", now);
        cmd.Parameters.AddWithValue("@mj", (object?)metadataJson ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();

        return unlockId;
    }

    public async Task<List<UnlockRecord>> GetUnlocksAsync(
        string gameId,
        string? ownerKind = null,
        string? ownerId = null,
        string? entityType = null,
        string? entityId = null)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();

        var conditions = new List<string> { "game_id=@g" };
        cmd.Parameters.AddWithValue("@g", gameId);

        if (ownerKind is not null) { conditions.Add("owner_kind=@ok"); cmd.Parameters.AddWithValue("@ok", ownerKind); }
        if (ownerId is not null) { conditions.Add("owner_id=@oi"); cmd.Parameters.AddWithValue("@oi", ownerId); }
        if (entityType is not null) { conditions.Add("entity_type=@et"); cmd.Parameters.AddWithValue("@et", entityType); }
        if (entityId is not null) { conditions.Add("entity_id=@ei"); cmd.Parameters.AddWithValue("@ei", entityId); }

        var where = string.Join(" AND ", conditions);
        cmd.CommandText = $"""
            SELECT id, game_id, owner_kind, owner_id, entity_type, entity_id, unlock_type, unlocked_at, metadata_json
            FROM unlock_records
            WHERE {where}
            ORDER BY unlocked_at ASC
            """;

        var results = new List<UnlockRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new UnlockRecord
            {
                Id = reader.GetString(0),
                GameId = reader.GetString(1),
                OwnerKind = reader.GetString(2),
                OwnerId = reader.GetString(3),
                EntityType = reader.GetString(4),
                EntityId = reader.GetString(5),
                UnlockType = reader.GetString(6),
                UnlockedAt = reader.GetString(7),
                MetadataJson = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        return results;
    }
}
