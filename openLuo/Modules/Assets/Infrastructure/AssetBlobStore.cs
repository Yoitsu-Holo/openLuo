using System.Security.Cryptography;
using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;
using Microsoft.Data.Sqlite;

namespace openLuo.Modules.Assets.Infrastructure;

public class AssetBlobStore(string connectionString) : IAssetBlobStore
{
    public async Task<string> PutAsync(string assetId, string mimeType, string blobRole, byte[] blobData, bool isPrimary)
    {
        var blobId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("O");
        var sha256 = Convert.ToHexString(SHA256.HashData(blobData)).ToLowerInvariant();
        var sizeBytes = blobData.LongLength;

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO asset_blob_payloads
                (id, asset_id, mime_type, blob_role, blob_data, size_bytes, sha256, is_primary, created_at)
            VALUES
                (@id, @aid, @mt, @br, @bd, @sz, @sha, @ip, @ca)
            """;
        insertCmd.Parameters.AddWithValue("@id", blobId);
        insertCmd.Parameters.AddWithValue("@aid", assetId);
        insertCmd.Parameters.AddWithValue("@mt", mimeType);
        insertCmd.Parameters.AddWithValue("@br", blobRole);
        insertCmd.Parameters.AddWithValue("@bd", blobData);
        insertCmd.Parameters.AddWithValue("@sz", sizeBytes);
        insertCmd.Parameters.AddWithValue("@sha", sha256);
        insertCmd.Parameters.AddWithValue("@ip", isPrimary ? 1 : 0);
        insertCmd.Parameters.AddWithValue("@ca", now);
        await insertCmd.ExecuteNonQueryAsync();

        await using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE asset_records SET updated_at=@now WHERE id=@id";
        updateCmd.Parameters.AddWithValue("@now", now);
        updateCmd.Parameters.AddWithValue("@id", assetId);
        await updateCmd.ExecuteNonQueryAsync();

        return blobId;
    }

    public async Task<List<AssetBlobPayload>> GetInfoByAssetIdAsync(string assetId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, asset_id, mime_type, blob_role, size_bytes, sha256, is_primary, created_at
            FROM asset_blob_payloads
            WHERE asset_id=@aid
            ORDER BY created_at ASC
            """;
        cmd.Parameters.AddWithValue("@aid", assetId);

        var results = new List<AssetBlobPayload>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new AssetBlobPayload
            {
                Id = reader.GetString(0),
                AssetId = reader.GetString(1),
                MimeType = reader.GetString(2),
                BlobRole = reader.GetString(3),
                SizeBytes = reader.GetInt64(4),
                Sha256 = reader.IsDBNull(5) ? null : reader.GetString(5),
                IsPrimary = reader.GetInt32(6) == 1,
                CreatedAt = reader.GetString(7)
            });
        }
        return results;
    }

    public async Task<byte[]?> GetDataAsync(string blobId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT blob_data FROM asset_blob_payloads WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", blobId);

        var result = await cmd.ExecuteScalarAsync();
        if (result is DBNull or null) return null;
        return (byte[])result;
    }
}
