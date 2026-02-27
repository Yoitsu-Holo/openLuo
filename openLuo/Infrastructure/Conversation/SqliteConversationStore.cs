using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;

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
        Blocks = Deserialize<List<object>>(row.BlocksJson),
        Metadata = Deserialize<Dictionary<string, string>>(row.MetadataJson) ?? new(StringComparer.OrdinalIgnoreCase)
    };

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
