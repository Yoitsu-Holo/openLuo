using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;
using Microsoft.Data.Sqlite;

namespace openLuo.Modules.Assets.Infrastructure;

public class AssetLinkService(string connectionString) : IAssetLinkService
{
    public async Task<string> CreateLinkAsync(
        string gameId,
        string fromEntityType,
        string fromEntityId,
        string toEntityType,
        string toEntityId,
        string linkType,
        string? metadataJson = null)
    {
        var linkId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("O");

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO entity_links
                (id, game_id, from_entity_type, from_entity_id, to_entity_type, to_entity_id, link_type, metadata_json, created_at)
            VALUES
                (@id, @g, @ft, @fi, @tt, @ti, @lt, @mj, @ca)
            """;
        cmd.Parameters.AddWithValue("@id", linkId);
        cmd.Parameters.AddWithValue("@g", gameId);
        cmd.Parameters.AddWithValue("@ft", fromEntityType);
        cmd.Parameters.AddWithValue("@fi", fromEntityId);
        cmd.Parameters.AddWithValue("@tt", toEntityType);
        cmd.Parameters.AddWithValue("@ti", toEntityId);
        cmd.Parameters.AddWithValue("@lt", linkType);
        cmd.Parameters.AddWithValue("@mj", (object?)metadataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", now);
        await cmd.ExecuteNonQueryAsync();

        return linkId;
    }

    public async Task<List<EntityLink>> GetLinksFromAsync(string fromEntityType, string fromEntityId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, from_entity_type, from_entity_id, to_entity_type, to_entity_id, link_type, metadata_json, created_at
            FROM entity_links
            WHERE from_entity_type=@ft AND from_entity_id=@fi
            ORDER BY created_at ASC
            """;
        cmd.Parameters.AddWithValue("@ft", fromEntityType);
        cmd.Parameters.AddWithValue("@fi", fromEntityId);

        return await ReadLinksAsync(cmd);
    }

    public async Task<List<EntityLink>> GetLinksToAsync(string toEntityType, string toEntityId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, from_entity_type, from_entity_id, to_entity_type, to_entity_id, link_type, metadata_json, created_at
            FROM entity_links
            WHERE to_entity_type=@tt AND to_entity_id=@ti
            ORDER BY created_at ASC
            """;
        cmd.Parameters.AddWithValue("@tt", toEntityType);
        cmd.Parameters.AddWithValue("@ti", toEntityId);

        return await ReadLinksAsync(cmd);
    }

    private static async Task<List<EntityLink>> ReadLinksAsync(SqliteCommand cmd)
    {
        var results = new List<EntityLink>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new EntityLink
            {
                Id = reader.GetString(0),
                FromEntityType = reader.GetString(1),
                FromEntityId = reader.GetString(2),
                ToEntityType = reader.GetString(3),
                ToEntityId = reader.GetString(4),
                LinkType = reader.GetString(5),
                MetadataJson = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.GetString(7)
            });
        }
        return results;
    }
}
