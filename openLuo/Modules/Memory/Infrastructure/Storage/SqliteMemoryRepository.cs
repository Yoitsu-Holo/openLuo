using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using openLuo.Infrastructure.Database;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Infrastructure.Storage;

/// <summary>
/// SQLite-backed repository for the rebuilt memory module.
/// Persists structured semantic fields into <c>memories</c> and vectors into <c>vec_memories</c>.
/// </summary>
public sealed class SqliteMemoryRepository : IMemoryRepository
{
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public SqliteMemoryRepository(IDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task StoreRecordAsync(MemoryRecord record, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO memories (
                id,
                game_id,
                character_id,
                memory_format_version,
                scope,
                source_text,
                summary,
                recall_text,
                tags_json,
                entities_json,
                metadata_json,
                emotion,
                importance,
                salience,
                emotional_weight,
                occurred_at,
                is_compressed)
            VALUES (
                @Id,
                @GameId,
                @CharacterId,
                @MemoryFormatVersion,
                @Scope,
                @SourceText,
                @Summary,
                @RecallText,
                @TagsJson,
                @EntitiesJson,
                @MetadataJson,
                @Emotion,
                @Importance,
                @Salience,
                @EmotionalWeight,
                @OccurredAt,
                @IsCompressed)
            """,
            new
            {
                Id = record.Id,
                GameId = record.GameId,
                CharacterId = record.OwnerCharacterId,
                MemoryFormatVersion = 2,
                Scope = (int)record.Scope,
                SourceText = record.SourceText,
                Summary = record.Summary,
                RecallText = record.RecallText,
                TagsJson = JsonSerializer.Serialize(record.Tags),
                EntitiesJson = JsonSerializer.Serialize(record.Entities),
                MetadataJson = JsonSerializer.Serialize(record.Metadata),
                Emotion = (int)record.Emotion,
                Importance = record.Importance,
                Salience = record.Salience,
                EmotionalWeight = MapEmotionalWeight(record),
                OccurredAt = record.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                IsCompressed = 0
            },
            cancellationToken: ct));
    }

    public async Task StoreEmbeddingAsync(MemoryRecord record, float[] embedding, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.OpenVecAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT OR REPLACE INTO vec_memories (memory_id, game_id, character_id, embedding)
            VALUES (@MemoryId, @GameId, @CharacterId, @Embedding)
            """,
            new
            {
                MemoryId = record.Id,
                GameId = record.GameId,
                CharacterId = record.OwnerCharacterId,
                Embedding = BuildVectorLiteral(embedding)
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<MemoryRecord>> QueryRecentAsync(
        string gameId,
        string? ownerCharacterId,
        IReadOnlyList<MemoryScope> scopes,
        int limit,
        CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.OpenAsync(ct);
        var includePrivate = scopes.Count == 0 || scopes.Contains(MemoryScope.CharacterPrivate);
        var includeShared = scopes.Contains(MemoryScope.Shared);

        var rows = await conn.QueryAsync<dynamic>(new CommandDefinition("""
            SELECT *
            FROM memories
            WHERE game_id = @GameId
              AND is_compressed = 0
              AND (
                    (@IncludePrivate = 1 AND character_id = @CharacterId)
                 OR (@IncludeShared = 1 AND (@CharacterId IS NULL OR character_id <> @CharacterId))
              )
            ORDER BY occurred_at DESC
            LIMIT @Limit
            """,
            new
            {
                GameId = gameId,
                CharacterId = ownerCharacterId,
                IncludePrivate = includePrivate ? 1 : 0,
                IncludeShared = includeShared ? 1 : 0,
                Limit = limit
            },
            cancellationToken: ct));

        return rows.Select(MapRow).ToArray();
    }

    private static int MapEmotionalWeight(MemoryRecord record)
    {
        // Keep the legacy integer emotional weight as a coarse scoring aid for SQL-side filtering.
        var weight = Math.Max(0, (int)Math.Round(record.Importance * 10.0f));
        return record.Emotion switch
        {
            MemoryEmotion.Positive => Math.Max(1, weight),
            MemoryEmotion.Negative => Math.Min(-1, -weight),
            MemoryEmotion.Mixed => Math.Max(1, weight),
            _ => 0
        };
    }

    private static string BuildVectorLiteral(float[] embedding)
    {
        var sb = new StringBuilder(embedding.Length * 12);
        sb.Append('[');
        for (var i = 0; i < embedding.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(embedding[i].ToString("G9", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static MemoryRecord MapRow(dynamic row)
    {
        var rowMap = (IDictionary<string, object?>)row;
        var sourceText = GetString(rowMap, "source_text");
        var summary = GetString(rowMap, "summary");
        var recallText = GetString(rowMap, "recall_text");
        var tags = ParseStringList(GetString(rowMap, "tags_json"));
        var entities = ParseStringList(GetString(rowMap, "entities_json"));
        var metadata = ParseStringDictionary(GetString(rowMap, "metadata_json"));

        return new MemoryRecord
        {
            Id = row.id,
            GameId = row.game_id,
            OwnerCharacterId = row.character_id,
            Scope = (MemoryScope)GetInt(rowMap, "scope", (int)MemoryScope.CharacterPrivate),
            OccurredAtUtc = DateTime.SpecifyKind(DateTime.Parse((string)row.occurred_at, CultureInfo.InvariantCulture), DateTimeKind.Utc),
            SourceText = sourceText,
            Summary = summary,
            RecallText = recallText,
            Tags = tags,
            Entities = entities,
            Emotion = (MemoryEmotion)GetInt(rowMap, "emotion", MapLegacyEmotion((int)row.emotional_weight)),
            Importance = GetFloat(rowMap, "importance", Math.Min(1.0f, Math.Abs((int)row.emotional_weight) / 10.0f)),
            Salience = GetFloat(rowMap, "salience", 0.5f),
            Metadata = metadata
        };
    }

    private static string GetString(IDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var raw) || raw is null || raw is DBNull)
            return string.Empty;
        return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static int GetInt(IDictionary<string, object?> row, string key, int fallback)
    {
        if (!row.TryGetValue(key, out var raw) || raw is null || raw is DBNull)
            return fallback;
        return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
    }

    private static float GetFloat(IDictionary<string, object?> row, string key, float fallback)
    {
        if (!row.TryGetValue(key, out var raw) || raw is null || raw is DBNull)
            return fallback;
        return Convert.ToSingle(raw, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> ParseStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyDictionary<string, string> ParseStringDictionary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static int MapLegacyEmotion(int emotionalWeight) =>
        emotionalWeight switch
        {
            > 0 => (int)MemoryEmotion.Positive,
            < 0 => (int)MemoryEmotion.Negative,
            _ => (int)MemoryEmotion.Neutral
        };
}
