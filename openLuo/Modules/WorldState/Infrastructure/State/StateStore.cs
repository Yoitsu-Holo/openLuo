using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;
using Microsoft.Data.Sqlite;

namespace openLuo.Modules.WorldState.Infrastructure.State;

public class StateStore(string connectionString) : IStateStore
{
    public async Task<string?> GetRawAsync(string gameId, StateOwnerKind ownerKind, string ownerId, string @namespace, string key)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT value_text FROM state_values
            WHERE game_id=@g AND owner_kind=@ok AND owner_id=@oi AND namespace=@ns AND key=@k
            """;
        cmd.Parameters.AddWithValue("@g", gameId);
        cmd.Parameters.AddWithValue("@ok", ownerKind.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@oi", ownerId);
        cmd.Parameters.AddWithValue("@ns", @namespace);
        cmd.Parameters.AddWithValue("@k", key);

        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : result.ToString();
    }

    public async Task SetAsync(string gameId, StateOwnerKind ownerKind, string ownerId, string @namespace, string key, string value)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await SetInternalAsync(conn, null, gameId, ownerKind, ownerId, @namespace, key, value);
    }

    private static async Task SetInternalAsync(SqliteConnection conn, SqliteTransaction? tx, string gameId, StateOwnerKind ownerKind, string ownerId, string @namespace, string key, string value)
    {
        await using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO state_values (game_id, owner_kind, owner_id, namespace, key, value_text, updated_at)
            VALUES (@g, @ok, @oi, @ns, @k, @v, @now)
            ON CONFLICT(game_id, owner_kind, owner_id, namespace, key)
            DO UPDATE SET value_text=excluded.value_text, updated_at=excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@g", gameId);
        cmd.Parameters.AddWithValue("@ok", ownerKind.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@oi", ownerId);
        cmd.Parameters.AddWithValue("@ns", @namespace);
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<StateValue>> QueryAsync(string gameId, StateOwnerKind? ownerKind, string? ownerId, string? @namespace, IEnumerable<string>? keys = null)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        var keysList = keys?.ToList();

        var conditions = new List<string> { "game_id=@g" };
        if (ownerKind.HasValue) conditions.Add("owner_kind=@ok");
        if (ownerId is not null) conditions.Add("owner_id=@oi");
        if (@namespace is not null) conditions.Add("namespace=@ns");
        if (keysList is { Count: > 0 })
        {
            var placeholders = string.Join(",", keysList.Select((_, i) => $"@k{i}"));
            conditions.Add($"key IN ({placeholders})");
        }

        var where = string.Join(" AND ", conditions);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT game_id, owner_kind, owner_id, namespace, key, value_text, updated_at FROM state_values WHERE {where}";
        cmd.Parameters.AddWithValue("@g", gameId);
        if (ownerKind.HasValue) cmd.Parameters.AddWithValue("@ok", ownerKind.Value.ToString().ToLowerInvariant());
        if (ownerId is not null) cmd.Parameters.AddWithValue("@oi", ownerId);
        if (@namespace is not null) cmd.Parameters.AddWithValue("@ns", @namespace);
        if (keysList is { Count: > 0 })
        {
            for (int i = 0; i < keysList.Count; i++)
                cmd.Parameters.AddWithValue($"@k{i}", keysList[i]);
        }

        var results = new List<StateValue>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var kindStr = reader.GetString(1);
            Enum.TryParse<StateOwnerKind>(kindStr, true, out var kind);
            results.Add(new StateValue
            {
                GameId = reader.GetString(0),
                OwnerKind = kind,
                OwnerId = reader.GetString(2),
                Namespace = reader.GetString(3),
                Key = reader.GetString(4),
                Value = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                UpdatedAt = reader.IsDBNull(6) ? null : reader.GetString(6),
                Defaulted = false
            });
        }
        return results;
    }

    public async Task SetBatchAsync(IEnumerable<(string gameId, StateOwnerKind ownerKind, string ownerId, string @namespace, string key, string value)> items)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync() as SqliteTransaction;

        foreach (var item in items)
            await SetInternalAsync(conn, tx, item.gameId, item.ownerKind, item.ownerId, item.@namespace, item.key, item.value);

        await tx!.CommitAsync();
    }

    public async Task LogChangeAsync(string gameId, StateOwnerKind ownerKind, string ownerId,
        string @namespace, string key, string? oldValue, string newValue,
        string changeType, string? reason, string? sourceType, string? sourceId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO state_change_logs
                (id, game_id, owner_kind, owner_id, namespace, key, old_value, new_value, change_type, reason, source_type, source_id, created_at)
            VALUES
                (@id, @g, @ok, @oi, @ns, @k, @ov, @nv, @ct, @r, @st, @si, @ca)
            """;
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@g", gameId);
        cmd.Parameters.AddWithValue("@ok", ownerKind.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@oi", ownerId);
        cmd.Parameters.AddWithValue("@ns", @namespace);
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@ov", (object?)oldValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nv", newValue);
        cmd.Parameters.AddWithValue("@ct", changeType);
        cmd.Parameters.AddWithValue("@r", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@st", (object?)sourceType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@si", (object?)sourceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }
}
