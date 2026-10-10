using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using openLuo.Core.Interfaces;

namespace openLuo.Infrastructure.Assets;

/// <summary>
/// 文件资产存储：元数据入 SQLite（WAL，<c>{root}/assets.db</c>），字节落盘到 <c>{root}/blobs/{id[0..2]}/{id}</c>。
/// 供协议 `POST/GET/DELETE /v1/assets` 使用（二进制经引用传递，见 §9）。
/// </summary>
public sealed class FileAssetStore : IAssetStore
{
    private readonly string _root;
    private readonly string _blobDir;
    private readonly long _maxBytes;
    private readonly ConcurrentDictionary<string, AssetInfo> _cache = new(StringComparer.Ordinal);

    public FileAssetStore(string rootDir, long maxBytes = 16L * 1024 * 1024)
    {
        _root = rootDir;
        _blobDir = Path.Combine(rootDir, "blobs");
        _maxBytes = maxBytes;

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_blobDir);
        InitializeDb();
    }

    private string DbPath => Path.Combine(_root, "assets.db");

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        return conn;
    }

    private void InitializeDb()
    {
        using var conn = Open();
        Execute(conn, "PRAGMA journal_mode=WAL;");
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS assets (
              id TEXT PRIMARY KEY,
              mime TEXT NOT NULL,
              size INTEGER NOT NULL,
              checksum TEXT NOT NULL,
              session_id TEXT,
              file_name TEXT,
              created_at_ms INTEGER NOT NULL
            );
            """);
        Execute(conn, "CREATE INDEX IF NOT EXISTS ix_assets_session ON assets(session_id);");
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public async Task<AssetInfo> PutAsync(
        ReadOnlyMemory<byte> bytes, string mime, string? sessionId = null, string? fileName = null,
        CancellationToken ct = default)
    {
        if (bytes.Length == 0)
            throw new InvalidOperationException("asset is empty");
        if (bytes.Length > _maxBytes)
            throw new InvalidOperationException($"asset exceeds limit: {bytes.Length} > {_maxBytes} bytes");

        var checksum = Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();
        var id = "ast_" + checksum[..32];

        var path = PathFor(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, bytes.ToArray(), ct).ConfigureAwait(false);

        var info = new AssetInfo(id, mime, bytes.Length, checksum, sessionId, fileName, DateTimeOffset.UtcNow);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO assets (id, mime, size, checksum, session_id, file_name, created_at_ms)
            VALUES ($id, $mime, $size, $cs, $sess, $name, $ts)
            ON CONFLICT(id) DO UPDATE SET mime = excluded.mime, session_id = COALESCE(assets.session_id, excluded.session_id);
            """;
        cmd.Parameters.AddWithValue("$id", info.Id);
        cmd.Parameters.AddWithValue("$mime", info.Mime);
        cmd.Parameters.AddWithValue("$size", info.Size);
        cmd.Parameters.AddWithValue("$cs", info.Checksum);
        cmd.Parameters.AddWithValue("$sess", (object?)sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", (object?)fileName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", info.CreatedAt.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();

        _cache[info.Id] = info;
        return info;
    }

    public async Task<AssetBlob?> GetAsync(string id, CancellationToken ct = default)
    {
        var info = Stat(id);
        if (info is null)
            return null;

        var path = PathFor(id);
        if (!File.Exists(path))
            return null;

        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        return new AssetBlob(info, bytes);
    }

    public AssetInfo? Stat(string id)
    {
        if (_cache.TryGetValue(id, out var cached))
            return cached;

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, mime, size, checksum, session_id, file_name, created_at_ms FROM assets WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var info = new AssetInfo(
            reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)));
        _cache[id] = info;
        return info;
    }

    public bool Delete(string id)
    {
        var path = PathFor(id);
        var removed = false;
        if (File.Exists(path))
        {
            try { File.Delete(path); removed = true; } catch { }
        }

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM assets WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        removed |= cmd.ExecuteNonQuery() > 0;

        _cache.TryRemove(id, out _);
        return removed;
    }

    public int PurgeExpired(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
            return 0;

        var cutoff = DateTimeOffset.UtcNow - ttl;
        var stale = new List<string>();

        using (var conn = Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM assets WHERE created_at_ms < $cutoff;";
            cmd.Parameters.AddWithValue("$cutoff", cutoff.ToUnixTimeMilliseconds());
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                stale.Add(reader.GetString(0));
        }

        foreach (var id in stale)
            Delete(id);

        return stale.Count;
    }

    private string PathFor(string id) =>
        Path.Combine(_blobDir, id.Length >= 2 ? id[..2] : "00", id);
}
