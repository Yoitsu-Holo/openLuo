using Dapper;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Infrastructure.ErrorHandling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace openLuo.Infrastructure.Database;

public class CharacterRepository(string connectionString, ILogger<CharacterRepository> logger) : ICharacterRepository
{
    private readonly string _pooledConnectionString =
        new SqliteConnectionStringBuilder(connectionString) { Pooling = true }.ToString();

    public async Task<Character?> GetByArchetypeIdAsync(string archetypeId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                """
                SELECT *
                FROM characters
                WHERE archetype_id = @ArchetypeId
                ORDER BY updated_at DESC, created_at DESC
                LIMIT 1
                """,
                new { ArchetypeId = archetypeId });

            if (row is null) return null;

            return MapCharacter(row);
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"GetByArchetypeIdAsync({archetypeId})");
            throw;
        }
    }

    public async Task<Character?> GetByIdAsync(string gameId, string characterId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                """
                SELECT *
                FROM characters
                WHERE id = @CharacterId
                  AND (game_id = @GameId OR IFNULL(game_id, '') = '')
                ORDER BY CASE WHEN game_id = @GameId THEN 0 ELSE 1 END, updated_at DESC
                LIMIT 1
                """,
                new { GameId = gameId, CharacterId = characterId });

            if (row is null) return null;
            return MapCharacter(row);
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"GetByIdAsync({gameId}, {characterId})");
            throw;
        }
    }

    public async Task<IReadOnlyList<Character>> ListByGameIdAsync(string gameId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            var rows = await conn.QueryAsync<dynamic>(
                """
                SELECT *
                FROM characters
                WHERE (game_id = @GameId OR IFNULL(game_id, '') = '')
                  AND IFNULL(is_enabled, 1) = 1
                ORDER BY display_priority ASC, updated_at DESC, name ASC
                """,
                new { GameId = gameId });
            return rows.Select(MapCharacter).ToList();
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"ListByGameIdAsync({gameId})");
            throw;
        }
    }

    public async Task SaveAsync(Character character, CancellationToken ct = default)
    {
        try
        {
            character.UpdatedAt = DateTime.UtcNow;
            if (character.CreatedAt == default)
                character.CreatedAt = character.UpdatedAt;

            await using var conn = new SqliteConnection(_pooledConnectionString);
            await conn.ExecuteAsync("""
                INSERT INTO characters (
                    id, game_id, archetype_id, name, display_priority, is_enabled,
                    role_profile_json, agent_policy_json, created_at, updated_at
                )
                VALUES (
                    @Id, @GameId, @ArchetypeId, @Name, @DisplayPriority, @IsEnabled,
                    @RoleProfileJson, @AgentPolicyJson, @CreatedAt, @UpdatedAt
                )
                ON CONFLICT(id) DO UPDATE SET
                    game_id = excluded.game_id,
                    name = excluded.name,
                    archetype_id = excluded.archetype_id,
                    display_priority = excluded.display_priority,
                    is_enabled = excluded.is_enabled,
                    role_profile_json = excluded.role_profile_json,
                    agent_policy_json = excluded.agent_policy_json,
                    updated_at = excluded.updated_at
                """,
                new
                {
                    character.Id,
                    character.GameId,
                    character.ArchetypeId,
                    character.Name,
                    character.DisplayPriority,
                    IsEnabled = character.IsEnabled ? 1 : 0,
                    character.RoleProfileJson,
                    character.AgentPolicyJson,
                    CreatedAt = character.CreatedAt.ToString("O"),
                    UpdatedAt = character.UpdatedAt.ToString("O")
                });
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"SaveAsync({character.Id})");
            throw;
        }
    }

    public async Task RecordAffectionEventAsync(AffectionEvent evt, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            await conn.ExecuteAsync("""
                INSERT INTO affection_events (id, character_id, reason, delta, occurred_at)
                VALUES (@Id, @CharacterId, @Reason, @Delta, @OccurredAt)
                """,
                new { evt.Id, evt.CharacterId, evt.Reason, evt.Delta, OccurredAt = evt.OccurredAt.ToString("O") });
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"RecordAffectionEventAsync({evt.Id})");
            throw;
        }
    }

    private static Character MapCharacter(dynamic row)
    {
        var createdAt = row.created_at is not null ? DateTime.Parse((string)row.created_at) : DateTime.UtcNow;
        var updatedAt = row.updated_at is not null ? DateTime.Parse((string)row.updated_at) : createdAt;
        return new Character
        {
            Id = row.id,
            GameId = row.game_id is not null ? (string)row.game_id : "",
            ArchetypeId = row.archetype_id,
            Name = row.name,
            DisplayPriority = row.display_priority is not null ? (int)row.display_priority : 100,
            IsEnabled = row.is_enabled is null || (int)row.is_enabled != 0,
            RoleProfileJson = row.role_profile_json is not null ? (string)row.role_profile_json : "{}",
            AgentPolicyJson = row.agent_policy_json is not null ? (string)row.agent_policy_json : "{}",
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }
}
