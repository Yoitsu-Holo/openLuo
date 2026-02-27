using openLuo.Modules.Assets.Core.Models;
using Microsoft.Data.Sqlite;

namespace openLuo.Modules.Assets.Infrastructure;

public class AssetDefStore(string connectionString)
{
    public void Upsert(AssetDef def)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO asset_defs
                (id, asset_type, namespace, mime_family, plugin_id, metadata_json, created_at, updated_at)
            VALUES
                (@id, @at, @ns, @mf, @pid, @mj, @now, @now)
            ON CONFLICT(id) DO UPDATE SET
                mime_family = excluded.mime_family,
                plugin_id = excluded.plugin_id,
                metadata_json = excluded.metadata_json,
                updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@id", def.DefinitionId);
        cmd.Parameters.AddWithValue("@at", def.AssetType);
        cmd.Parameters.AddWithValue("@ns", def.Namespace);
        cmd.Parameters.AddWithValue("@mf", (object?)def.MimeFamily ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pid", (object?)def.PluginId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mj", (object?)def.MetadataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AssetDef> LoadAll()
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT asset_type, namespace, mime_family, plugin_id, metadata_json
                FROM asset_defs
                ORDER BY asset_type, namespace
                """;

            using var reader = cmd.ExecuteReader();
            var defs = new List<AssetDef>();
            while (reader.Read())
            {
                defs.Add(new AssetDef
                {
                    AssetType = reader.GetString(0),
                    Namespace = reader.GetString(1),
                    MimeFamily = reader.IsDBNull(2) ? null : reader.GetString(2),
                    PluginId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    MetadataJson = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }

            return defs;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("no such table: asset_defs", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }
    }
}
