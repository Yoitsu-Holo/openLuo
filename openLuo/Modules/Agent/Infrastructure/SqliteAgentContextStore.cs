using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Agent.Infrastructure;

public sealed class SqliteAgentContextStore(string connectionString, IRuntimeConfigCenter? configCenter = null) : IAgentContextStore
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder(connectionString) { Pooling = true }.ToString();
    private int ConversationRetainCount => Math.Max(1, configCenter?.GetSnapshot().Agent.ContextConversationRetainCount ?? 24);

    public async Task<AgentContext> GetOrCreateAsync(string gameId, string characterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            """
            SELECT game_id, character_id, summary, created_at, updated_at
            FROM agent_contexts
            WHERE game_id = @GameId AND character_id = @CharacterId
            LIMIT 1
            """,
            new { GameId = gameId, CharacterId = characterId },
            cancellationToken: ct));

        var context = row is null
            ? new AgentContext
            {
                GameId = gameId,
                CharacterId = characterId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }
            : new AgentContext
            {
                GameId = row.game_id,
                CharacterId = row.character_id,
                Summary = row.summary ?? string.Empty,
                CreatedAtUtc = DateTimeOffset.Parse((string)row.created_at),
                UpdatedAtUtc = DateTimeOffset.Parse((string)row.updated_at)
            };

        var turns = await conn.QueryAsync<dynamic>(new CommandDefinition(
            """
            SELECT speaker_id, speaker_role, content, timestamp_utc
            FROM agent_context_turns
            WHERE game_id = @GameId AND character_id = @CharacterId
            ORDER BY timestamp_utc ASC
            LIMIT 32
            """,
            new { GameId = gameId, CharacterId = characterId },
            cancellationToken: ct));

        foreach (var turn in turns)
        {
            context.Conversation.Add(new AgentConversationTurn
            {
                SpeakerId = turn.speaker_id,
                SpeakerRole = turn.speaker_role,
                Content = turn.content,
                TimestampUtc = DateTimeOffset.Parse((string)turn.timestamp_utc)
            });
        }

        return context;
    }

    public async Task SaveAsync(AgentContext context, CancellationToken ct = default)
    {
        context.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (context.Conversation.Count > ConversationRetainCount)
        {
            var trimmed = context.Conversation.Take(context.Conversation.Count - ConversationRetainCount).ToList();
            var summaryBits = new List<string>();
            if (!string.IsNullOrWhiteSpace(context.Summary))
                summaryBits.AddRange(context.Summary.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            summaryBits.AddRange(trimmed.Select(x => $"{x.SpeakerRole}/{x.SpeakerId}: {x.Content}"));
            context.Summary = string.Join(" | ", summaryBits.TakeLast(10));
            context.Conversation.RemoveRange(0, context.Conversation.Count - ConversationRetainCount);
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO agent_contexts (game_id, character_id, summary, created_at, updated_at)
            VALUES (@GameId, @CharacterId, @Summary, @CreatedAt, @UpdatedAt)
            ON CONFLICT(game_id, character_id) DO UPDATE SET
                summary = excluded.summary,
                updated_at = excluded.updated_at
            """,
            new
            {
                context.GameId,
                context.CharacterId,
                context.Summary,
                CreatedAt = context.CreatedAtUtc.ToString("O"),
                UpdatedAt = context.UpdatedAtUtc.ToString("O")
            },
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM agent_context_turns
            WHERE game_id = @GameId AND character_id = @CharacterId
            """,
            new { context.GameId, context.CharacterId },
            transaction: tx,
            cancellationToken: ct));

        foreach (var turn in context.Conversation)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO agent_context_turns (
                    id, game_id, character_id, speaker_id, speaker_role, content, timestamp_utc
                )
                VALUES (
                    @Id, @GameId, @CharacterId, @SpeakerId, @SpeakerRole, @Content, @TimestampUtc
                )
                """,
                new
                {
                    Id = Guid.NewGuid().ToString("N"),
                    context.GameId,
                    context.CharacterId,
                    turn.SpeakerId,
                    turn.SpeakerRole,
                    turn.Content,
                    TimestampUtc = turn.TimestampUtc.ToString("O")
                },
                transaction: tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }
}
