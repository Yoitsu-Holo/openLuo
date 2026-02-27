using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;
using Microsoft.Data.Sqlite;

namespace openLuo.Modules.Assets.Infrastructure;

public class AssetMetaStore(string connectionString) : IAssetMetaStore
{
    public async Task<string> PutAsync(string assetId, string metaType, string payloadJson)
    {
        var metaId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("O");

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO asset_meta_json (id, asset_id, meta_type, payload_json, created_at)
            VALUES (@id, @aid, @mt, @pj, @ca)
            """;
        cmd.Parameters.AddWithValue("@id", metaId);
        cmd.Parameters.AddWithValue("@aid", assetId);
        cmd.Parameters.AddWithValue("@mt", metaType);
        cmd.Parameters.AddWithValue("@pj", payloadJson);
        cmd.Parameters.AddWithValue("@ca", now);
        await cmd.ExecuteNonQueryAsync();

        return metaId;
    }

    public async Task<List<AssetMetaJson>> GetByAssetIdAsync(string assetId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, asset_id, meta_type, payload_json, created_at
            FROM asset_meta_json
            WHERE asset_id=@aid
            ORDER BY created_at ASC
            """;
        cmd.Parameters.AddWithValue("@aid", assetId);

        var results = new List<AssetMetaJson>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new AssetMetaJson
            {
                Id = reader.GetString(0),
                AssetId = reader.GetString(1),
                MetaType = reader.GetString(2),
                PayloadJson = reader.GetString(3),
                CreatedAt = reader.GetString(4)
            });
        }
        return results;
    }
}
