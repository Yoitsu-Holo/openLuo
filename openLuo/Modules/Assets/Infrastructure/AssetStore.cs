using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;
using Microsoft.Data.Sqlite;

namespace openLuo.Modules.Assets.Infrastructure;

public class AssetStore(string connectionString) : IAssetStore
{
    public async Task<string> CreateAsync(AssetRecord record)
    {
        var id = string.IsNullOrEmpty(record.Id) ? Guid.NewGuid().ToString() : record.Id;
        var now = DateTime.UtcNow.ToString("O");

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO asset_records
                (id, game_id, asset_type, namespace, owner_kind, owner_id, label, status, source_type, created_at, updated_at)
            VALUES
                (@id, @g, @at, @ns, @ok, @oi, @lbl, @st, @src, @ca, @ua)
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@g", record.GameId);
        cmd.Parameters.AddWithValue("@at", record.AssetType);
        cmd.Parameters.AddWithValue("@ns", record.Namespace);
        cmd.Parameters.AddWithValue("@ok", record.OwnerKind);
        cmd.Parameters.AddWithValue("@oi", record.OwnerId);
        cmd.Parameters.AddWithValue("@lbl", (object?)record.Label ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@st", record.Status);
        cmd.Parameters.AddWithValue("@src", record.SourceType);
        cmd.Parameters.AddWithValue("@ca", now);
        cmd.Parameters.AddWithValue("@ua", now);

        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    public async Task<AssetRecord?> GetByIdAsync(string assetId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, game_id, asset_type, namespace, owner_kind, owner_id, label, status, source_type, created_at, updated_at
            FROM asset_records
            WHERE id=@id
            """;
        cmd.Parameters.AddWithValue("@id", assetId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return MapRecord(reader);
    }

    public async Task<List<AssetRecord>> QueryAsync(
        string gameId,
        string? assetType = null,
        string? @namespace = null,
        string? ownerKind = null,
        string? ownerId = null,
        string? sourceType = null,
        string? labelLike = null,
        string? linkedToEntityType = null,
        string? linkedToEntityId = null,
        int limit = 50,
        int offset = 0)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();

        var conditions = new List<string> { "ar.game_id=@g" };
        cmd.Parameters.AddWithValue("@g", gameId);

        if (assetType is not null) { conditions.Add("ar.asset_type=@at"); cmd.Parameters.AddWithValue("@at", assetType); }
        if (@namespace is not null) { conditions.Add("ar.namespace=@ns"); cmd.Parameters.AddWithValue("@ns", @namespace); }
        if (ownerKind is not null) { conditions.Add("ar.owner_kind=@ok"); cmd.Parameters.AddWithValue("@ok", ownerKind); }
        if (ownerId is not null) { conditions.Add("ar.owner_id=@oi"); cmd.Parameters.AddWithValue("@oi", ownerId); }
        if (sourceType is not null) { conditions.Add("ar.source_type=@src"); cmd.Parameters.AddWithValue("@src", sourceType); }
        if (labelLike is not null) { conditions.Add("ar.label LIKE @lbl"); cmd.Parameters.AddWithValue("@lbl", $"%{labelLike}%"); }

        string fromClause;
        if (linkedToEntityType is not null && linkedToEntityId is not null)
        {
            fromClause = """
                asset_records ar
                INNER JOIN entity_links el ON el.to_entity_id = ar.id
                    AND el.to_entity_type = @ett
                    AND el.from_entity_type = @eft
                    AND el.from_entity_id = @efi
                """;
            cmd.Parameters.AddWithValue("@ett", "asset");
            cmd.Parameters.AddWithValue("@eft", linkedToEntityType);
            cmd.Parameters.AddWithValue("@efi", linkedToEntityId);
        }
        else
        {
            fromClause = "asset_records ar";
        }

        var where = string.Join(" AND ", conditions);
        cmd.CommandText = $"""
            SELECT ar.id, ar.game_id, ar.asset_type, ar.namespace, ar.owner_kind, ar.owner_id,
                   ar.label, ar.status, ar.source_type, ar.created_at, ar.updated_at
            FROM {fromClause}
            WHERE {where}
            ORDER BY ar.created_at DESC
            LIMIT @limit OFFSET @offset
            """;
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        var results = new List<AssetRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapRecord(reader));

        return results;
    }

    public async Task UpdateTimestampAsync(string assetId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE asset_records SET updated_at=@now WHERE id=@id";
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", assetId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static AssetRecord MapRecord(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        GameId = reader.GetString(1),
        AssetType = reader.GetString(2),
        Namespace = reader.GetString(3),
        OwnerKind = reader.GetString(4),
        OwnerId = reader.GetString(5),
        Label = reader.IsDBNull(6) ? null : reader.GetString(6),
        Status = reader.GetString(7),
        SourceType = reader.GetString(8),
        CreatedAt = reader.GetString(9),
        UpdatedAt = reader.GetString(10)
    };
}
