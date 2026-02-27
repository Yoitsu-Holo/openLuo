using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using openLuo.Core.Interfaces;
using openLuo.Infrastructure.Database;
using openLuo.Modules.Embedding.Core.Interfaces;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Infrastructure.Retrieval;

/// <summary>
/// Vector-first retriever backed by sqlite-vec.
/// It embeds the semantic search text and joins vector hits back to structured memory rows.
/// </summary>
public sealed class VectorMemoryRetriever : IMemoryRetriever
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly IGameLogger? _logger;

    public VectorMemoryRetriever(
        IDatabaseConnectionFactory connectionFactory,
        IEmbeddingClient embeddingClient,
        IGameLogger? logger = null)
    {
        _connectionFactory = connectionFactory;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public async Task<MemoryRecallResult> RetrieveAsync(SemanticRecallQuery query, CancellationToken ct = default)
    {
        if (!_embeddingClient.Enabled)
            return new MemoryRecallResult
            {
                Success = false,
                Trace = ["retriever:vector", "embedding:disabled"],
                Degraded = true
            };

        var embedding = await _embeddingClient.EmbedAsync(BuildSearchText(query), ct);
        await using var conn = await _connectionFactory.OpenVecAsync(ct);

        var includePrivate = query.Scopes.Count == 0 || query.Scopes.Contains(MemoryScope.CharacterPrivate);
        var includeShared = query.Scopes.Contains(MemoryScope.Shared);

        var rows = await conn.QueryAsync<dynamic>(new CommandDefinition("""
            WITH vec_hits AS (
                SELECT memory_id, character_id, distance
                FROM vec_memories
                WHERE embedding MATCH @Embedding
                  AND k = @TopK
                  AND game_id = @GameId
                  AND (
                        (@IncludePrivate = 1 AND character_id = @CharacterId)
                     OR (@IncludeShared = 1 AND character_id <> @CharacterId)
                  )
            )
            SELECT m.*, h.distance
            FROM vec_hits h
            JOIN memories m ON m.id = h.memory_id
            WHERE m.game_id = @GameId
              AND m.is_compressed = 0
            ORDER BY h.distance
            LIMIT @TopK
            """,
            new
            {
                query.GameId,
                query.CharacterId,
                query.TopK,
                IncludePrivate = includePrivate ? 1 : 0,
                IncludeShared = includeShared ? 1 : 0,
                Embedding = BuildVectorLiteral(embedding)
            },
            cancellationToken: ct));

        var records = rows.Select(MapRow).ToArray();
        _logger?.Debug("memory", $"VectorMemoryRetriever completed results={records.Length}");

        return new MemoryRecallResult
        {
            Success = true,
            Records = records,
            Summary = string.Join("\n", records.Select(static r => $"- {r.Summary}")),
            Trace =
            [
                "retriever:vector",
                $"matchCount:{records.Length}"
            ],
            Degraded = false
        };
    }

    private static string BuildSearchText(SemanticRecallQuery query) =>
        // The vector query is intentionally formed from the caller-visible semantic query only.
        // It does not invent hidden context here.
        string.Join('\n', new[] { query.SearchText, string.Join(' ', query.QueryTags) }.Where(static s => !string.IsNullOrWhiteSpace(s)));

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

        double? distance = TryGetDistance(row);
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
            Salience = distance.HasValue ? Math.Max(0.0f, 1.0f - (float)distance.Value) : 0.5f,
            Metadata = distance.HasValue
                ? MergeMetadata(metadata, new Dictionary<string, string> { ["distance"] = distance.Value.ToString(CultureInfo.InvariantCulture) })
                : metadata
        };
    }

    private static double? TryGetDistance(dynamic row)
    {
        if (row is IDictionary<string, object?> values
            && values.TryGetValue("distance", out var raw)
            && raw is not null
            && raw is not DBNull)
        {
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }

        return null;
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

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        var merged = new Dictionary<string, string>(left, StringComparer.Ordinal);
        foreach (var (key, value) in right)
            merged[key] = value;
        return merged;
    }

    private static int MapLegacyEmotion(int emotionalWeight) =>
        emotionalWeight switch
        {
            > 0 => (int)MemoryEmotion.Positive,
            < 0 => (int)MemoryEmotion.Negative,
            _ => (int)MemoryEmotion.Neutral
        };
}
