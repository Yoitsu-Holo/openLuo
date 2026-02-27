using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace openLuo.Modules.WorldState.Infrastructure.Timeline;

public class TimelineService(string connectionString) : ITimelineService
{
    public async Task<TimelineEvent> CreateAsync(string gameId, TimelineCreateRequest request, CancellationToken ct = default)
    {
        if (!TimelineLimits.TryValidateCreateRequest(request, out var error))
            throw new ArgumentException(error);

        var now = DateTime.UtcNow.ToString("O");
        var evt = new TimelineEvent
        {
            Id = Guid.NewGuid().ToString(),
            GameId = gameId,
            EventType = request.EventType.Trim(),
            Title = request.Title.Trim(),
            DueAtEpochMs = request.DueAtEpochMs,
            EndAtEpochMs = request.EndAtEpochMs,
            RecurrenceRule = request.RecurrenceRule?.Trim(),
            Status = TimelineEventStatus.Pending,
            ActionJson = request.ActionJson,
            ContextJson = request.ContextJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO timeline_events
                (id, game_id, event_type, title, due_at_epoch_ms, end_at_epoch_ms, recurrence_rule,
                 status, action_json, context_json, created_at, updated_at)
            VALUES
                (@id, @g, @et, @title, @due, @end, @rr, @status, @action, @ctx, @ca, @ua)
            """;
        cmd.Parameters.AddWithValue("@id", evt.Id);
        cmd.Parameters.AddWithValue("@g", evt.GameId);
        cmd.Parameters.AddWithValue("@et", evt.EventType);
        cmd.Parameters.AddWithValue("@title", evt.Title);
        cmd.Parameters.AddWithValue("@due", evt.DueAtEpochMs);
        cmd.Parameters.AddWithValue("@end", (object?)evt.EndAtEpochMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rr", (object?)evt.RecurrenceRule ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", evt.Status);
        cmd.Parameters.AddWithValue("@action", (object?)evt.ActionJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ctx", (object?)evt.ContextJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", evt.CreatedAt);
        cmd.Parameters.AddWithValue("@ua", evt.UpdatedAt);
        await cmd.ExecuteNonQueryAsync(ct);

        return evt;
    }

    public async Task<List<TimelineEvent>> QueryAsync(string gameId, TimelineQueryOptions options, CancellationToken ct = default)
    {
        var events = new List<TimelineEvent>();
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        var conditions = new List<string> { "game_id = @g" };
        cmd.Parameters.AddWithValue("@g", gameId);

        if (!string.IsNullOrWhiteSpace(options.EventType))
        {
            conditions.Add("event_type = @et");
            cmd.Parameters.AddWithValue("@et", options.EventType);
        }

        if (!string.IsNullOrWhiteSpace(options.Status))
        {
            conditions.Add("status = @st");
            cmd.Parameters.AddWithValue("@st", options.Status);
        }

        var limit = Math.Clamp(options.Limit, 1, 500);
        var offset = Math.Max(0, options.Offset);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        cmd.CommandText = $"""
            SELECT id, game_id, event_type, title, due_at_epoch_ms, end_at_epoch_ms, recurrence_rule,
                   status, action_json, context_json, created_at, updated_at
            FROM timeline_events
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY due_at_epoch_ms ASC, created_at ASC
            LIMIT @limit OFFSET @offset
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            events.Add(Map(reader));

        return events;
    }

    public async Task<List<TimelineEvent>> PollDueAsync(string gameId, long nowEpochMs, int limit = 32, CancellationToken ct = default)
    {
        var dueEvents = new List<TimelineEvent>();

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var clampedLimit = Math.Clamp(limit, 1, 200);

        await using (var select = conn.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)tx;
            select.CommandText = """
                SELECT id, game_id, event_type, title, due_at_epoch_ms, end_at_epoch_ms, recurrence_rule,
                       status, action_json, context_json, created_at, updated_at
                FROM timeline_events
                WHERE game_id = @g
                  AND status = @pending
                  AND due_at_epoch_ms <= @now
                ORDER BY due_at_epoch_ms ASC, created_at ASC
                LIMIT @limit
                """;
            select.Parameters.AddWithValue("@g", gameId);
            select.Parameters.AddWithValue("@pending", TimelineEventStatus.Pending);
            select.Parameters.AddWithValue("@now", nowEpochMs);
            select.Parameters.AddWithValue("@limit", clampedLimit);

            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                dueEvents.Add(Map(reader));
        }

        if (dueEvents.Count > 0)
        {
            var placeholders = string.Join(",", dueEvents.Select((_, i) => $"@id{i}"));
            await using var update = conn.CreateCommand();
            update.Transaction = (SqliteTransaction)tx;
            update.CommandText = $"""
                UPDATE timeline_events
                SET status = @active,
                    updated_at = @ua
                WHERE game_id = @g
                  AND status = @pending
                  AND id IN ({placeholders})
                """;
            update.Parameters.AddWithValue("@active", TimelineEventStatus.Active);
            update.Parameters.AddWithValue("@pending", TimelineEventStatus.Pending);
            update.Parameters.AddWithValue("@ua", DateTime.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("@g", gameId);
            for (var i = 0; i < dueEvents.Count; i++)
                update.Parameters.AddWithValue($"@id{i}", dueEvents[i].Id);

            await update.ExecuteNonQueryAsync(ct);
            foreach (var evt in dueEvents)
            {
                evt.Status = TimelineEventStatus.Active;
                evt.UpdatedAt = DateTime.UtcNow.ToString("O");
            }
        }

        await tx.CommitAsync(ct);
        return dueEvents;
    }

    public async Task<bool> AckAsync(string gameId, string eventId, string finalStatus = TimelineEventStatus.Done, CancellationToken ct = default)
    {
        var target = NormalizeAckStatus(finalStatus);
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var select = conn.CreateCommand();
        select.Transaction = (SqliteTransaction)tx;
        select.CommandText = """
            SELECT id, game_id, event_type, title, due_at_epoch_ms, end_at_epoch_ms, recurrence_rule,
                   status, action_json, context_json, created_at, updated_at
            FROM timeline_events
            WHERE game_id = @g AND id = @id
            LIMIT 1
            """;
        select.Parameters.AddWithValue("@g", gameId);
        select.Parameters.AddWithValue("@id", eventId);

        TimelineEvent? existing = null;
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
                existing = Map(reader);
        }

        if (existing is null
            || (existing.Status != TimelineEventStatus.Pending && existing.Status != TimelineEventStatus.Active))
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            UPDATE timeline_events
            SET status = @target,
                updated_at = @ua
            WHERE game_id = @g
              AND id = @id
              AND status IN (@pending, @active)
            """;
        cmd.Parameters.AddWithValue("@target", target);
        cmd.Parameters.AddWithValue("@ua", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@g", gameId);
        cmd.Parameters.AddWithValue("@id", eventId);
        cmd.Parameters.AddWithValue("@pending", TimelineEventStatus.Pending);
        cmd.Parameters.AddWithValue("@active", TimelineEventStatus.Active);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected <= 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        if (target is TimelineEventStatus.Done or TimelineEventStatus.Missed
            && TryComputeNextDue(existing.DueAtEpochMs, existing.RecurrenceRule, out var nextDueAtEpochMs))
        {
            var nextEvent = BuildNextRecurringEvent(existing, nextDueAtEpochMs);
            await using var insert = conn.CreateCommand();
            insert.Transaction = (SqliteTransaction)tx;
            insert.CommandText = """
                INSERT INTO timeline_events
                    (id, game_id, event_type, title, due_at_epoch_ms, end_at_epoch_ms, recurrence_rule,
                     status, action_json, context_json, created_at, updated_at)
                VALUES
                    (@id, @g, @et, @title, @due, @end, @rr, @status, @action, @ctx, @ca, @ua)
                """;
            insert.Parameters.AddWithValue("@id", nextEvent.Id);
            insert.Parameters.AddWithValue("@g", nextEvent.GameId);
            insert.Parameters.AddWithValue("@et", nextEvent.EventType);
            insert.Parameters.AddWithValue("@title", nextEvent.Title);
            insert.Parameters.AddWithValue("@due", nextEvent.DueAtEpochMs);
            insert.Parameters.AddWithValue("@end", (object?)nextEvent.EndAtEpochMs ?? DBNull.Value);
            insert.Parameters.AddWithValue("@rr", (object?)nextEvent.RecurrenceRule ?? DBNull.Value);
            insert.Parameters.AddWithValue("@status", nextEvent.Status);
            insert.Parameters.AddWithValue("@action", (object?)nextEvent.ActionJson ?? DBNull.Value);
            insert.Parameters.AddWithValue("@ctx", (object?)nextEvent.ContextJson ?? DBNull.Value);
            insert.Parameters.AddWithValue("@ca", nextEvent.CreatedAt);
            insert.Parameters.AddWithValue("@ua", nextEvent.UpdatedAt);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> CancelAsync(string gameId, string eventId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE timeline_events
            SET status = @cancelled,
                updated_at = @ua
            WHERE game_id = @g
              AND id = @id
              AND status IN (@pending, @active)
            """;
        cmd.Parameters.AddWithValue("@cancelled", TimelineEventStatus.Cancelled);
        cmd.Parameters.AddWithValue("@ua", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@g", gameId);
        cmd.Parameters.AddWithValue("@id", eventId);
        cmd.Parameters.AddWithValue("@pending", TimelineEventStatus.Pending);
        cmd.Parameters.AddWithValue("@active", TimelineEventStatus.Active);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    private static string NormalizeAckStatus(string status)
    {
        if (status.Equals(TimelineEventStatus.Missed, StringComparison.OrdinalIgnoreCase))
            return TimelineEventStatus.Missed;
        if (status.Equals(TimelineEventStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
            return TimelineEventStatus.Cancelled;
        return TimelineEventStatus.Done;
    }

    private static TimelineEvent BuildNextRecurringEvent(TimelineEvent source, long nextDueAtEpochMs)
    {
        var duration = source.EndAtEpochMs.HasValue
            ? source.EndAtEpochMs.Value - source.DueAtEpochMs
            : (long?)null;
        var now = DateTime.UtcNow.ToString("O");
        return new TimelineEvent
        {
            Id = Guid.NewGuid().ToString(),
            GameId = source.GameId,
            EventType = source.EventType,
            Title = source.Title,
            DueAtEpochMs = nextDueAtEpochMs,
            EndAtEpochMs = duration.HasValue ? nextDueAtEpochMs + duration.Value : null,
            RecurrenceRule = source.RecurrenceRule,
            Status = TimelineEventStatus.Pending,
            ActionJson = source.ActionJson,
            ContextJson = source.ContextJson,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static bool TryComputeNextDue(long currentDueAtEpochMs, string? recurrenceRule, out long nextDueAtEpochMs)
    {
        nextDueAtEpochMs = 0;
        if (string.IsNullOrWhiteSpace(recurrenceRule))
            return false;

        var rule = recurrenceRule.Trim().ToLowerInvariant();
        long deltaMs = rule switch
        {
            "hourly" => 3_600_000L,
            "daily" => 86_400_000L,
            "weekly" => 7 * 86_400_000L,
            _ => 0
        };

        if (deltaMs <= 0)
        {
            var m = Regex.Match(rule, @"^every:(\d+)(m|h|d)$", RegexOptions.IgnoreCase);
            if (!m.Success || !long.TryParse(m.Groups[1].Value, out var amount) || amount <= 0)
                return false;
            deltaMs = m.Groups[2].Value.ToLowerInvariant() switch
            {
                "m" => amount * 60_000L,
                "h" => amount * 3_600_000L,
                "d" => amount * 86_400_000L,
                _ => 0
            };
        }

        if (deltaMs <= 0) return false;
        nextDueAtEpochMs = currentDueAtEpochMs + deltaMs;
        return true;
    }

    private static TimelineEvent Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        GameId = reader.GetString(1),
        EventType = reader.GetString(2),
        Title = reader.GetString(3),
        DueAtEpochMs = reader.GetInt64(4),
        EndAtEpochMs = reader.IsDBNull(5) ? null : reader.GetInt64(5),
        RecurrenceRule = reader.IsDBNull(6) ? null : reader.GetString(6),
        Status = reader.GetString(7),
        ActionJson = reader.IsDBNull(8) ? null : reader.GetString(8),
        ContextJson = reader.IsDBNull(9) ? null : reader.GetString(9),
        CreatedAt = reader.GetString(10),
        UpdatedAt = reader.GetString(11)
    };
}
