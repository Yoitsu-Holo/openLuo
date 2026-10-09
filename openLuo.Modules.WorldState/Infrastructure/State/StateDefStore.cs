using System.Text.Json;
using openLuo.Modules.WorldState.Core.Models;
using Microsoft.Data.Sqlite;

namespace openLuo.Modules.WorldState.Infrastructure.State;

public class StateDefStore(string connectionString)
{
    public void Upsert(StateDef def)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        EnsureStateDefsTable(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO state_defs
                (id, namespace, key, owner_kind, value_type, default_value, min_value, max_value,
                 derived, mutable_by_llm, status_group, status_order, hidden_from_status,
                 display_format, prompt_context, plugin_id, metadata_json, enum_values,
                 lifecycle_state, retirement_policy, source_kind, source_ref, created_at, updated_at)
            VALUES
                (@id, @ns, @key, @ok, @vt, @dv, @min, @max, @derived, @mbl, @sg, @so, @hidden,
                 @df, @pc, @pid, @mj, @enumValues, @lifecycle, @retirement, @sourceKind, @sourceRef, @now, @now)
            ON CONFLICT(id) DO UPDATE SET
                value_type = excluded.value_type,
                default_value = excluded.default_value,
                min_value = excluded.min_value,
                max_value = excluded.max_value,
                derived = excluded.derived,
                mutable_by_llm = excluded.mutable_by_llm,
                status_group = excluded.status_group,
                status_order = excluded.status_order,
                hidden_from_status = excluded.hidden_from_status,
                display_format = excluded.display_format,
                prompt_context = excluded.prompt_context,
                plugin_id = excluded.plugin_id,
                metadata_json = excluded.metadata_json,
                enum_values = excluded.enum_values,
                source_kind = excluded.source_kind,
                source_ref = excluded.source_ref,
                updated_at = excluded.updated_at
            """;
        var (metadataJson, enumValuesJson) = BuildMetadata(def);
        cmd.Parameters.AddWithValue("@id", def.DefinitionId);
        cmd.Parameters.AddWithValue("@ns", def.Namespace);
        cmd.Parameters.AddWithValue("@key", def.Key);
        cmd.Parameters.AddWithValue("@ok", def.OwnerKind.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@vt", def.ValueType.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@dv", (object?)def.DefaultValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@min", (object?)def.MinValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@max", (object?)def.MaxValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@derived", def.Derived ? 1 : 0);
        cmd.Parameters.AddWithValue("@mbl", def.MutableByLlm ? 1 : 0);
        cmd.Parameters.AddWithValue("@sg", (object?)def.StatusGroup ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@so", def.StatusOrder);
        cmd.Parameters.AddWithValue("@hidden", def.HiddenFromStatus ? 1 : 0);
        cmd.Parameters.AddWithValue("@df", (object?)def.DisplayFormat ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pc", (object?)def.PromptContext ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pid", (object?)def.PluginId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mj", (object?)metadataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@enumValues", enumValuesJson);
        cmd.Parameters.AddWithValue("@lifecycle", ToDb(def.LifecycleState));
        cmd.Parameters.AddWithValue("@retirement", ToDb(def.RetirementPolicy));
        cmd.Parameters.AddWithValue("@sourceKind", (object?)def.SourceKind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sourceRef", (object?)def.SourceRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public bool UpdateLifecycle(
        string definitionId,
        ResourceLifecycleState lifecycleState,
        ResourceRetirementPolicy retirementPolicy)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        EnsureStateDefsTable(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE state_defs
            SET lifecycle_state=@lifecycle,
                retirement_policy=@retirement,
                updated_at=@now
            WHERE id=@id
            """;
        cmd.Parameters.AddWithValue("@id", definitionId);
        cmd.Parameters.AddWithValue("@lifecycle", ToDb(lifecycleState));
        cmd.Parameters.AddWithValue("@retirement", ToDb(retirementPolicy));
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        return cmd.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<StateDef> LoadAll()
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        EnsureStateDefsTable(conn);

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT namespace, key, owner_kind, value_type, default_value, min_value, max_value,
                       derived, mutable_by_llm, status_group, status_order, hidden_from_status,
                       display_format, prompt_context, plugin_id, metadata_json, enum_values,
                       lifecycle_state, retirement_policy, source_kind, source_ref
                FROM state_defs
                ORDER BY namespace, owner_kind, key
                """;

            using var reader = cmd.ExecuteReader();
            var defs = new List<StateDef>();
            while (reader.Read())
            {
                Enum.TryParse<StateOwnerKind>(reader.GetString(2), true, out var ownerKind);
                Enum.TryParse<StateValueType>(reader.GetString(3), true, out var valueType);
                var metadataJson = reader.IsDBNull(15) ? null : reader.GetString(15);
                var enumValuesJson = reader.IsDBNull(16) ? "[]" : reader.GetString(16);
                var lifecycle = reader.IsDBNull(17) ? ResourceLifecycleState.Active : ParseLifecycle(reader.GetString(17));
                var retirement = reader.IsDBNull(18) ? ResourceRetirementPolicy.KeepValue : ParseRetirement(reader.GetString(18));

                defs.Add(new StateDef
                {
                    Namespace = reader.GetString(0),
                    Key = reader.GetString(1),
                    OwnerKind = ownerKind,
                    ValueType = valueType,
                    DefaultValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                    MinValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                    MaxValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Derived = reader.GetInt32(7) == 1,
                    MutableByLlm = reader.GetInt32(8) == 1,
                    StatusGroup = reader.IsDBNull(9) ? null : reader.GetString(9),
                    StatusOrder = reader.GetInt32(10),
                    HiddenFromStatus = reader.GetInt32(11) == 1,
                    DisplayFormat = reader.IsDBNull(12) ? null : reader.GetString(12),
                    PromptContext = reader.IsDBNull(13) ? null : reader.GetString(13),
                    PluginId = reader.IsDBNull(14) ? null : reader.GetString(14),
                    MetadataJson = metadataJson,
                    EnumValues = JsonSerializer.Deserialize<List<string>>(enumValuesJson) ?? [],
                    LifecycleState = lifecycle,
                    RetirementPolicy = retirement,
                    SourceKind = reader.IsDBNull(19) ? null : reader.GetString(19),
                    SourceRef = reader.IsDBNull(20) ? null : reader.GetString(20)
                });
            }

            return defs;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("no such table: state_defs", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }
    }

    private static (string? metadataJson, string enumValuesJson) BuildMetadata(StateDef def)
    {
        using var doc = string.IsNullOrWhiteSpace(def.MetadataJson)
            ? null
            : JsonDocument.Parse(def.MetadataJson);
        var payload = doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Serialize(JsonSerializer.Deserialize<Dictionary<string, object?>>(doc.RootElement.GetRawText()))
            : def.MetadataJson;
        var enumValuesJson = JsonSerializer.Serialize(def.EnumValues);
        return (payload, enumValuesJson);
    }

    private static void EnsureStateDefsTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS state_defs (
                id                  TEXT PRIMARY KEY,
                namespace           TEXT NOT NULL,
                key                 TEXT NOT NULL,
                owner_kind          TEXT NOT NULL,
                value_type          TEXT NOT NULL,
                default_value       TEXT,
                min_value           TEXT,
                max_value           TEXT,
                derived             INTEGER NOT NULL DEFAULT 0,
                mutable_by_llm      INTEGER NOT NULL DEFAULT 0,
                status_group        TEXT,
                status_order        INTEGER NOT NULL DEFAULT 0,
                hidden_from_status  INTEGER NOT NULL DEFAULT 0,
                display_format      TEXT,
                prompt_context      TEXT,
                plugin_id           TEXT,
                metadata_json       TEXT,
                enum_values         TEXT NOT NULL DEFAULT '[]',
                lifecycle_state     TEXT NOT NULL DEFAULT 'active',
                retirement_policy   TEXT NOT NULL DEFAULT 'keep_value',
                source_kind         TEXT,
                source_ref          TEXT,
                created_at          TEXT NOT NULL,
                updated_at          TEXT NOT NULL,
                UNIQUE(namespace, key, owner_kind)
            )
            """;
        cmd.ExecuteNonQuery();
    }

    private static string ToDb(ResourceLifecycleState state) =>
        state.ToString().ToLowerInvariant();

    private static string ToDb(ResourceRetirementPolicy policy) =>
        policy switch
        {
            ResourceRetirementPolicy.HideValue => "hide_value",
            ResourceRetirementPolicy.PurgeValue => "purge_value",
            _ => "keep_value"
        };

    private static ResourceLifecycleState ParseLifecycle(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "hidden" => ResourceLifecycleState.Hidden,
            "frozen" => ResourceLifecycleState.Frozen,
            "retired" => ResourceLifecycleState.Retired,
            _ => ResourceLifecycleState.Active
        };

    private static ResourceRetirementPolicy ParseRetirement(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "hide_value" => ResourceRetirementPolicy.HideValue,
            "purge_value" => ResourceRetirementPolicy.PurgeValue,
            _ => ResourceRetirementPolicy.KeepValue
        };
}
