using Dapper;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Infrastructure.ErrorHandling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace openLuo.Infrastructure.Database;

public class GameStateRepository(
    string connectionString,
    ILogger<GameStateRepository> logger,
    IGameContextAccessor? gameContextAccessor = null) : IGameStateRepository
{
    private readonly string _pooledConnectionString =
        new SqliteConnectionStringBuilder(connectionString) { Pooling = true }.ToString();

    public async Task<GameState?> GetAsync(CancellationToken ct = default)
    {
        var currentGameId = gameContextAccessor?.Current?.GameId;
        if (string.IsNullOrWhiteSpace(currentGameId))
            return null;

        return await GetAsync(currentGameId, ct);
    }

    public async Task<GameState?> GetAsync(string gameId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                """
                SELECT *
                FROM game_state
                WHERE id = @GameId
                LIMIT 1
                """,
                new { GameId = gameId });

            return row is null ? null : MapState(row);
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"GetAsync({gameId})");
            throw;
        }
    }

    public async Task<IReadOnlyList<GameState>> ListAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_pooledConnectionString);
            var rows = await conn.QueryAsync<dynamic>(
                """
                SELECT *
                FROM game_state
                ORDER BY updated_at DESC, created_at DESC, id ASC
                """);

            return rows.Select(MapState).ToList();
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, "ListAsync");
            throw;
        }
    }

    public async Task SaveAsync(GameState state, CancellationToken ct = default)
    {
        try
        {
            state.UpdatedAt = DateTime.UtcNow;
            await using var conn = new SqliteConnection(_pooledConnectionString);
            await conn.ExecuteAsync("""
                INSERT INTO game_state (id, player_name, archetype_id, active_character_id, current_location, current_day, current_minute, last_interaction_day, created_at, updated_at)
                VALUES (@Id, @PlayerName, @ArchetypeId, @ActiveCharacterId, @CurrentLocation, @CurrentDay, @CurrentMinute, @LastInteractionDay, @CreatedAt, @UpdatedAt)
                ON CONFLICT(id) DO UPDATE SET
                    current_location = excluded.current_location,
                    active_character_id = excluded.active_character_id,
                    current_day = excluded.current_day,
                    current_minute = excluded.current_minute,
                    last_interaction_day = excluded.last_interaction_day,
                    updated_at = excluded.updated_at
                """,
                new
                {
                    state.Id,
                    state.PlayerName,
                    state.ArchetypeId,
                    state.ActiveCharacterId,
                    state.CurrentLocation,
                    state.CurrentDay,
                    state.CurrentMinute,
                    state.LastInteractionDay,
                    CreatedAt = state.CreatedAt.ToString("O"),
                    UpdatedAt = state.UpdatedAt.ToString("O")
                });
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(logger, ex, $"SaveAsync({state.Id})");
            throw;
        }
    }

    private static GameState MapState(dynamic row)
    {
        return new GameState
        {
            Id = row.id,
            PlayerName = row.player_name,
            ArchetypeId = row.archetype_id,
            ActiveCharacterId = row.active_character_id is not null ? (string)row.active_character_id : "",
            CurrentLocation = row.current_location is not null ? (string)row.current_location : "",
            CurrentDay = (int)row.current_day,
            CurrentMinute = row.current_minute is not null ? (int)row.current_minute : 480,
            LastInteractionDay = row.last_interaction_day is not null ? (int)row.last_interaction_day : (int)row.current_day,
            CreatedAt = DateTime.Parse((string)row.created_at),
            UpdatedAt = DateTime.Parse((string)row.updated_at)
        };
    }
}
