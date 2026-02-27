using Dapper;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Agent.Core.Models;
using openLuo.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace openLuo.Modules.Agent.Infrastructure;

public sealed class PartyTaskRepository(IDatabaseConnectionFactory connectionFactory) : IPartyTaskRepository
{

    public async Task<string> CreateTaskAsync(string gameId, string title, string requestedBy, string contextJson, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow.ToString("O");
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO party_tasks (id, game_id, title, requested_by, status, context_json, created_at, updated_at)
            VALUES (@Id, @GameId, @Title, @RequestedBy, 'running', @ContextJson, @CreatedAt, @UpdatedAt)
            """,
            new
            {
                Id = id,
                GameId = gameId,
                Title = title,
                RequestedBy = requestedBy,
                ContextJson = contextJson,
                CreatedAt = now,
                UpdatedAt = now
            },
            cancellationToken: ct));
        return id;
    }

    public async Task CreateStepsAsync(string taskId, IReadOnlyList<PartyTaskStepRecord> steps, CancellationToken ct = default)
    {
        if (steps.Count == 0)
            return;

        await using var conn = await connectionFactory.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var step in steps)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO party_task_steps (
                    id, task_id, step_order, assigned_character_id, role, instruction,
                    result_json, status, started_at, finished_at, created_at, updated_at
                )
                VALUES (
                    @Id, @TaskId, @StepOrder, @AssignedCharacterId, @Role, @Instruction,
                    @ResultJson, @Status, @StartedAt, @FinishedAt, @CreatedAt, @UpdatedAt
                )
                """,
                step,
                transaction: tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    public async Task UpdateStepResultAsync(string stepId, string status, string resultJson, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("O");
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE party_task_steps
            SET status = @Status,
                result_json = @ResultJson,
                started_at = COALESCE(started_at, @Now),
                finished_at = @Now,
                updated_at = @Now
            WHERE id = @StepId
            """,
            new { StepId = stepId, Status = status, ResultJson = resultJson, Now = now },
            cancellationToken: ct));
    }

    public async Task UpdateTaskStatusAsync(string taskId, string status, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("O");
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE party_tasks
            SET status = @Status,
                updated_at = @Now
            WHERE id = @TaskId
            """,
            new { TaskId = taskId, Status = status, Now = now },
            cancellationToken: ct));
    }

    public async Task<PartyTaskRecord?> GetTaskAsync(string gameId, string taskId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            """
            SELECT id, game_id, title, requested_by, status, context_json, created_at, updated_at
            FROM party_tasks
            WHERE game_id = @GameId AND id = @TaskId
            LIMIT 1
            """,
            new { GameId = gameId, TaskId = taskId },
            cancellationToken: ct));
        return row is null ? null : MapTask(row);
    }

    public async Task<IReadOnlyList<PartyTaskRecord>> ListRecentTasksAsync(string gameId, int limit = 5, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<dynamic>(new CommandDefinition(
            """
            SELECT id, game_id, title, requested_by, status, context_json, created_at, updated_at
            FROM party_tasks
            WHERE game_id = @GameId
            ORDER BY created_at DESC
            LIMIT @Limit
            """,
            new { GameId = gameId, Limit = Math.Max(1, limit) },
            cancellationToken: ct));
        return rows.Select(MapTask).ToList();
    }

    public async Task<IReadOnlyList<PartyTaskStepRecord>> ListStepsAsync(string taskId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<dynamic>(new CommandDefinition(
            """
            SELECT id, task_id, step_order, assigned_character_id, role, instruction,
                   result_json, status, started_at, finished_at, created_at, updated_at
            FROM party_task_steps
            WHERE task_id = @TaskId
            ORDER BY step_order ASC
            """,
            new { TaskId = taskId },
            cancellationToken: ct));
        return rows.Select(MapStep).ToList();
    }

    private static PartyTaskRecord MapTask(dynamic row) => new()
    {
        Id = row.id,
        GameId = row.game_id,
        Title = row.title,
        RequestedBy = row.requested_by,
        Status = row.status,
        ContextJson = row.context_json,
        CreatedAt = DateTime.Parse((string)row.created_at),
        UpdatedAt = DateTime.Parse((string)row.updated_at)
    };

    private static PartyTaskStepRecord MapStep(dynamic row) => new()
    {
        Id = row.id,
        TaskId = row.task_id,
        StepOrder = (int)row.step_order,
        AssignedCharacterId = row.assigned_character_id,
        Role = row.role,
        Instruction = row.instruction,
        ResultJson = row.result_json,
        Status = row.status,
        StartedAt = row.started_at is null ? null : DateTime.Parse((string)row.started_at),
        FinishedAt = row.finished_at is null ? null : DateTime.Parse((string)row.finished_at),
        CreatedAt = DateTime.Parse((string)row.created_at),
        UpdatedAt = DateTime.Parse((string)row.updated_at)
    };
}
