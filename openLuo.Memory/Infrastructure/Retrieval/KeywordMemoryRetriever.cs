using System.Globalization;
using System.Text.Json;
using Dapper;
using openLuo.Infrastructure.Database;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Infrastructure.Retrieval;

/// <summary>
/// Lexical fallback retriever based on structured semantic text fields.
/// It prefers <c>recall_text</c>, <c>summary</c>, and <c>source_text</c>.
/// </summary>
public sealed class KeywordMemoryRetriever : IMemoryRetriever
{
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public KeywordMemoryRetriever(IDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<MemoryRecallResult> RetrieveAsync(SemanticRecallQuery query, CancellationToken ct = default)
    {
        var terms = BuildKeywordTerms(query);
        await using var conn = await _connectionFactory.OpenAsync(ct);

        var rows = await QueryKeywordRowsAsync(conn, query, terms, ct);
        var ranked = RankRows(rows, query, terms)
            .Take(query.TopK)
            .ToArray();

        return new MemoryRecallResult
        {
            Success = true,
            Records = ranked,
            Summary = string.Join("\n", ranked.Select(static r => $"- {r.Summary}")),
            Trace =
            [
                "retriever:keyword",
                $"candidateCount:{rows.Count}",
                $"matchCount:{ranked.Length}"
            ],
            Degraded = false
        };
    }

    private static async Task<IReadOnlyList<dynamic>> QueryKeywordRowsAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        SemanticRecallQuery query,
        IReadOnlyList<string> terms,
        CancellationToken ct)
    {
        var includePrivate = query.Scopes.Count == 0 || query.Scopes.Contains(MemoryScope.CharacterPrivate);
        var includeShared = query.Scopes.Contains(MemoryScope.Shared);
        var parameters = new DynamicParameters(new
        {
            query.GameId,
            CharacterId = query.CharacterId,
            IncludePrivate = includePrivate ? 1 : 0,
            IncludeShared = includeShared ? 1 : 0,
            CandidateLimit = Math.Max(query.TopK * 8, 32)
        });

        var sql = new System.Text.StringBuilder("""
            SELECT *
            FROM memories
            WHERE game_id = @GameId
              AND is_compressed = 0
              AND (
                    (@IncludePrivate = 1 AND character_id = @CharacterId)
                 OR (@IncludeShared = 1 AND character_id <> @CharacterId)
              )
            """);

        if (query.MinImportance.HasValue)
        {
            sql.AppendLine("  AND ABS(emotional_weight) >= @MinWeight");
            parameters.Add("MinWeight", Math.Max(0, (int)Math.Round(query.MinImportance.Value * 10.0f)));
        }

        if (terms.Count > 0)
        {
            sql.AppendLine("  AND (");
            for (var i = 0; i < terms.Count; i++)
            {
                if (i > 0)
                    sql.AppendLine("    OR");
                // Structured fields have clear roles:
                // recall_text: search-oriented material
                // summary: prompt-facing short form
                // source_text: original factual event text
                sql.AppendLine($"    recall_text LIKE @Like{i} ESCAPE '\\' OR summary LIKE @Like{i} ESCAPE '\\' OR source_text LIKE @Like{i} ESCAPE '\\'");
                parameters.Add($"Like{i}", BuildLikeQuery(terms[i]));
            }
            sql.AppendLine("  )");
        }

        sql.AppendLine("ORDER BY occurred_at DESC");
        sql.AppendLine("LIMIT @CandidateLimit");

        var rows = (await conn.QueryAsync<dynamic>(new CommandDefinition(sql.ToString(), parameters, cancellationToken: ct))).ToList();
        if (rows.Count > 0 || terms.Count == 0)
            return rows;

        return (await conn.QueryAsync<dynamic>(new CommandDefinition("""
            SELECT *
            FROM memories
            WHERE game_id = @GameId
              AND is_compressed = 0
              AND (
                    (@IncludePrivate = 1 AND character_id = @CharacterId)
                 OR (@IncludeShared = 1 AND character_id <> @CharacterId)
              )
            ORDER BY occurred_at DESC
            LIMIT @CandidateLimit
            """,
            parameters,
            cancellationToken: ct))).ToList();
    }

    private static IEnumerable<MemoryRecord> RankRows(
        IReadOnlyList<dynamic> rows,
        SemanticRecallQuery query,
        IReadOnlyList<string> terms)
    {
        return rows
            .Select(MapRow)
            .Select(record => new
            {
                Record = record,
                Score = Score(record, query, terms)
            })
            .Where(static x => x.Score > 0)
            .OrderByDescending(static x => x.Score)
            .ThenByDescending(static x => x.Record.OccurredAtUtc)
            .Select(static x => x.Record);
    }

    private static double Score(MemoryRecord record, SemanticRecallQuery query, IReadOnlyList<string> terms)
    {
        var score = 0.0;
        var haystack = $"{record.RecallText}\n{record.Summary}\n{record.SourceText}";

        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 3.0 + Math.Min(term.Length, 8) * 0.1;
            else
                score += ComputeFuzzySimilarity(haystack, term);
        }

        if (query.PreferImportant)
            score += record.Importance;
        if (query.PreferEmotionallySalient)
            score += record.Salience * 0.5;
        if (query.PreferRecent)
            score += Math.Max(0, 1.0 - Math.Min(30.0, (DateTime.UtcNow - record.OccurredAtUtc).TotalDays) / 30.0);

        return score;
    }

    private static MemoryRecord MapRow(dynamic row)
    {
        var rowMap = (IDictionary<string, object?>)row;
        var characterId = (string)row.character_id;
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
            OwnerCharacterId = characterId,
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
            Metadata = MergeMetadata(metadata, new Dictionary<string, string> { ["ownerCharacterId"] = characterId })
        };
    }

    private static string BuildLikeQuery(string query)
    {
        var escaped = query
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static IReadOnlyList<string> BuildKeywordTerms(SemanticRecallQuery query)
    {
        var raw = query.QueryTags.Append(query.SearchText)
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(static s => s.Split([' ', '\n', '\r', '\t', '，', '。', '：', ':', '、', ',', '！', '？', '|', '=', '-'], StringSplitOptions.RemoveEmptyEntries))
            .Where(static s => s.Length >= 2)
            .ToList();

        var expanded = new List<string>(raw);
        foreach (var term in raw)
            expanded.AddRange(BuildCjkNgrams(term));

        return expanded.Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray();
    }

    private static IEnumerable<string> BuildCjkNgrams(string term)
    {
        var compact = new string(term.Where(static ch => !char.IsWhiteSpace(ch)).ToArray());
        if (compact.Length < 2 || !compact.Any(static ch => ch is >= '\u4E00' and <= '\u9FFF'))
            yield break;

        var maxGram = Math.Min(4, compact.Length);
        for (var size = 2; size <= maxGram; size++)
        {
            for (var i = 0; i <= compact.Length - size; i++)
                yield return compact.Substring(i, size);
        }
    }

    private static double ComputeFuzzySimilarity(string content, string term)
    {
        var maxSimilarity = 0.0;
        var minWindow = Math.Max(1, term.Length - 1);
        var maxWindow = Math.Min(content.Length, term.Length + 1);

        for (var windowSize = minWindow; windowSize <= maxWindow; windowSize++)
        {
            for (var i = 0; i <= content.Length - windowSize; i++)
            {
                var window = content.Substring(i, windowSize);
                var similarity = ComputeCharacterDice(window, term);
                if (similarity > maxSimilarity)
                    maxSimilarity = similarity;
            }
        }

        return maxSimilarity >= 0.45 ? maxSimilarity : 0.0;
    }

    private static double ComputeCharacterDice(string left, string right)
    {
        var leftCounts = CountCharacters(left);
        var rightCounts = CountCharacters(right);
        var overlap = 0;

        foreach (var (key, leftCount) in leftCounts)
        {
            if (rightCounts.TryGetValue(key, out var rightCount))
                overlap += Math.Min(leftCount, rightCount);
        }

        return (2.0 * overlap) / Math.Max(1, left.Length + right.Length);
    }

    private static Dictionary<char, int> CountCharacters(string value)
    {
        var result = new Dictionary<char, int>();
        foreach (var ch in value.Where(static ch => !char.IsWhiteSpace(ch)))
            result[ch] = result.TryGetValue(ch, out var count) ? count + 1 : 1;
        return result;
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
