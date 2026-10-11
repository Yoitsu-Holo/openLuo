using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Core.Models;

namespace openLuo.Infrastructure.Conversation;

public sealed class SqliteConversationStore : IConversationStore
{
    private readonly string _connectionString;
    public SqliteConversationStore(string connectionString) => _connectionString = connectionString;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            CREATE TABLE IF NOT EXISTS conversation_turns (
                session_id TEXT NOT NULL, turn_id TEXT NOT NULL, speaker_id TEXT NOT NULL,
                speaker_name TEXT NOT NULL, speaker_role TEXT NOT NULL, content TEXT NOT NULL,
                timestamp_utc TEXT NOT NULL, game_day INTEGER NULL, game_minute INTEGER NULL,
                blocks_json TEXT NULL, metadata_json TEXT NULL,
                PRIMARY KEY (session_id, turn_id)
            );
            CREATE INDEX IF NOT EXISTS ix_conversation_turns_session_time
                ON conversation_turns(session_id, timestamp_utc DESC);
            """, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(string sessionId, int limit, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        var rows = await connection.QueryAsync<ConversationRow>(new CommandDefinition("""
            SELECT session_id SessionId, turn_id TurnId, speaker_id SpeakerId, speaker_name SpeakerName,
                   speaker_role SpeakerRole, content Content, timestamp_utc TimestampUtc,
                   game_day GameDay, game_minute GameMinute, blocks_json BlocksJson, metadata_json MetadataJson
            FROM conversation_turns WHERE session_id = @SessionId
            ORDER BY timestamp_utc DESC LIMIT @Limit;
            """, new { SessionId = sessionId, Limit = Math.Max(1, limit) }, cancellationToken: ct));
        return rows.Reverse().Select(ToTurn).ToList();
    }

    public async Task AppendAsync(ConversationTurn turn, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO conversation_turns
                (session_id, turn_id, speaker_id, speaker_name, speaker_role, content, timestamp_utc, game_day, game_minute, blocks_json, metadata_json)
            VALUES (@SessionId, @TurnId, @SpeakerId, @SpeakerName, @SpeakerRole, @Content, @TimestampUtc, @GameDay, @GameMinute, @BlocksJson, @MetadataJson)
            ON CONFLICT(session_id, turn_id) DO UPDATE SET content = excluded.content, metadata_json = excluded.metadata_json;
            """, new
        {
            turn.SessionId, turn.TurnId, turn.SpeakerId, turn.SpeakerName, turn.SpeakerRole, turn.Content,
            TimestampUtc = turn.TimestampUtc.ToString("O"), turn.GameDay, turn.GameMinute,
            BlocksJson = turn.Blocks is { Count: > 0 } ? JsonSerializer.Serialize(turn.Blocks) : null,
            MetadataJson = turn.Metadata.Count > 0 ? JsonSerializer.Serialize(turn.Metadata) : null
        }, cancellationToken: ct));
    }

    private static ConversationTurn ToTurn(ConversationRow row) => new()
    {
        SessionId = row.SessionId, TurnId = row.TurnId, SpeakerId = row.SpeakerId, SpeakerName = row.SpeakerName,
        SpeakerRole = row.SpeakerRole, Content = row.Content, TimestampUtc = DateTimeOffset.Parse(row.TimestampUtc),
        GameDay = row.GameDay, GameMinute = row.GameMinute,
        Blocks = DeserializeBlocks(row.BlocksJson),
        Metadata = Deserialize<Dictionary<string, string>>(row.MetadataJson) ?? new(StringComparer.OrdinalIgnoreCase)
    };

    /// <summary>
    /// 反序列化 blocks_json 并按 Kind 重建具体 Block 类型（Text/Image/Asset/Video）。
    /// 不能用 Deserialize&lt;List&lt;object&gt;&gt;：System.Text.Json 会把对象还原为 JsonElement，
    /// 导致下游 is ImageBlock / OfType&lt;Block&gt; 全部落空（图片链路断点）。
    /// </summary>
    private static IReadOnlyList<object>? DeserializeBlocks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        List<JsonElement> elements;
        try
        {
            elements = JsonSerializer.Deserialize<List<JsonElement>>(json) ?? [];
        }
        catch
        {
            return null;
        }

        var blocks = new List<object>(elements.Count);
        foreach (var e in elements)
        {
            if (e.ValueKind != JsonValueKind.Object)
                continue;
            switch (ReadBlockKind(e))
            {
                case BlockKind.Text:
                    blocks.Add(new TextBlock
                    {
                        Kind = BlockKind.Text, Text = ReadString(e, "Text") ?? string.Empty,
                        Source = ReadSource(e), Visibility = ReadVisibility(e)
                    });
                    break;
                case BlockKind.Image:
                    blocks.Add(new ImageBlock
                    {
                        Kind = BlockKind.Image,
                        AssetId = ReadString(e, "AssetId") ?? string.Empty,
                        MimeType = ReadString(e, "MimeType") ?? "image/jpeg",
                        Name = ReadString(e, "Name"), AltText = ReadString(e, "AltText"),
                        Caption = ReadString(e, "Caption"), RenderHint = ReadString(e, "RenderHint"),
                        DataUri = ReadString(e, "DataUri"),
                        Source = ReadSource(e), Visibility = ReadVisibility(e)
                    });
                    break;
                case BlockKind.Asset:
                    blocks.Add(new AssetBlock
                    {
                        Kind = BlockKind.Asset,
                        AssetId = ReadString(e, "AssetId") ?? string.Empty,
                        MimeType = ReadString(e, "MimeType") ?? "application/octet-stream",
                        BlobRole = ReadString(e, "BlobRole") ?? "primary", Name = ReadString(e, "Name"),
                        Source = ReadSource(e), Visibility = ReadVisibility(e)
                    });
                    break;
                case BlockKind.Video:
                    blocks.Add(new VideoBlock
                    {
                        Kind = BlockKind.Video,
                        AssetId = ReadString(e, "AssetId") ?? string.Empty,
                        MimeType = ReadString(e, "MimeType") ?? "video/mp4",
                        Name = ReadString(e, "Name"), Caption = ReadString(e, "Caption"),
                        ThumbnailDataUri = ReadString(e, "ThumbnailDataUri"),
                        DurationSeconds = e.TryGetProperty("DurationSeconds", out var d) && d.TryGetDouble(out var dv) ? dv : null,
                        Source = ReadSource(e), Visibility = ReadVisibility(e)
                    });
                    break;
                default:
                    blocks.Add(e); // 未知类型保留原始元素，不阻塞历史读取
                    break;
            }
        }
        return blocks;
    }

    private static BlockKind? ReadBlockKind(JsonElement e)
    {
        if (!e.TryGetProperty("Kind", out var kind))
            return null;
        if (kind.ValueKind == JsonValueKind.String && Enum.TryParse<BlockKind>(kind.GetString(), out var s))
            return s;
        if (kind.ValueKind == JsonValueKind.Number && kind.TryGetInt32(out var n) && Enum.IsDefined(typeof(BlockKind), n))
            return (BlockKind)n;
        return null;
    }

    private static BlockSource ReadSource(JsonElement e)
    {
        if (!e.TryGetProperty("Source", out var s)) return BlockSource.Unknown;
        if (s.ValueKind == JsonValueKind.String && Enum.TryParse<BlockSource>(s.GetString(), out var parsed)) return parsed;
        if (s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var n) && Enum.IsDefined(typeof(BlockSource), n)) return (BlockSource)n;
        return BlockSource.Unknown;
    }

    private static OutputVisibility ReadVisibility(JsonElement e)
    {
        if (!e.TryGetProperty("Visibility", out var v)) return OutputVisibility.Public;
        if (v.ValueKind == JsonValueKind.String && Enum.TryParse<OutputVisibility>(v.GetString(), out var parsed)) return parsed;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && Enum.IsDefined(typeof(OutputVisibility), n)) return (OutputVisibility)n;
        return OutputVisibility.Public;
    }

    private static string? ReadString(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static T? Deserialize<T>(string? json) => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json);

    private sealed class ConversationRow
    {
        public string SessionId { get; init; } = string.Empty; public string TurnId { get; init; } = string.Empty;
        public string SpeakerId { get; init; } = string.Empty; public string SpeakerName { get; init; } = string.Empty;
        public string SpeakerRole { get; init; } = string.Empty; public string Content { get; init; } = string.Empty;
        public string TimestampUtc { get; init; } = string.Empty; public int? GameDay { get; init; } public int? GameMinute { get; init; }
        public string? BlocksJson { get; init; } public string? MetadataJson { get; init; }
    }
}
