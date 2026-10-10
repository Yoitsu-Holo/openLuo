using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using openLuo.Capabilities.Core;
using openLuo.Core.Interfaces;

namespace openLuo.Infrastructure.Persistence;

/// <summary>
/// Hub 控制面状态持久化（<c>hub.db</c>，WAL，独立于业务 <c>game.db</c>）：
/// 会话元数据、作业、调度、已签发令牌。供 Hub 重启后保守恢复（决策 #3）。
/// </summary>
public sealed class HubStore : ITokenStore, ITraceStore
{
    private readonly string _dbPath;

    public HubStore(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        Initialize();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = Open();
        Execute(conn, "PRAGMA journal_mode=WAL;");
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS sessions (
              session_id TEXT PRIMARY KEY, subject_id TEXT NOT NULL, agent_id TEXT NOT NULL,
              conversation_id TEXT NOT NULL, client_type TEXT, client_id TEXT,
              created_at_ms INTEGER NOT NULL, updated_at_ms INTEGER NOT NULL
            );
            """);
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS jobs (
              id TEXT PRIMARY KEY, kind TEXT NOT NULL, status TEXT NOT NULL, progress REAL NOT NULL,
              message TEXT, session_id TEXT, created_at_ms INTEGER NOT NULL, completed_at_ms INTEGER
            );
            """);
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS schedules (
              id TEXT PRIMARY KEY, session_id TEXT NOT NULL, kind TEXT NOT NULL,
              at_ms INTEGER, cron TEXT, enabled INTEGER NOT NULL, next_run_at_ms INTEGER, payload TEXT
            );
            """);
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS tokens (
              token TEXT PRIMARY KEY, client_id TEXT NOT NULL, role TEXT NOT NULL, expires_at_ms INTEGER NOT NULL
            );
            """);
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS traces (
              turn_id TEXT PRIMARY KEY, session_id TEXT NOT NULL, origin TEXT,
              started_at_ms INTEGER NOT NULL, ended_at_ms INTEGER, success INTEGER,
              termination_reason TEXT, final_text TEXT
            );
            """);
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS trace_events (
              turn_id TEXT NOT NULL, seq INTEGER NOT NULL, ts_ms INTEGER NOT NULL,
              type TEXT NOT NULL, data TEXT, PRIMARY KEY (turn_id, seq)
            );
            """);
        Execute(conn, "CREATE INDEX IF NOT EXISTS ix_trace_events_ts ON trace_events(ts_ms);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS ix_traces_started ON traces(started_at_ms);");
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ── 会话 ────────────────────────────────────────────────────

    public void UpsertSession(AgentSession session, string? clientType, string? clientId, DateTimeOffset updatedAt)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (session_id, subject_id, agent_id, conversation_id, client_type, client_id, created_at_ms, updated_at_ms)
            VALUES ($id, $sub, $agent, $conv, $ct, $ci, $created, $updated)
            ON CONFLICT(session_id) DO UPDATE SET
              subject_id = excluded.subject_id, agent_id = excluded.agent_id, conversation_id = excluded.conversation_id,
              client_type = excluded.client_type, client_id = excluded.client_id, updated_at_ms = excluded.updated_at_ms;
            """;
        cmd.Parameters.AddWithValue("$id", session.SessionId);
        cmd.Parameters.AddWithValue("$sub", session.SubjectId);
        cmd.Parameters.AddWithValue("$agent", session.AgentId);
        cmd.Parameters.AddWithValue("$conv", session.ConversationId);
        cmd.Parameters.AddWithValue("$ct", (object?)clientType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ci", (object?)clientId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", updatedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$updated", updatedAt.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }

    public void DeleteSession(string sessionId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sessions WHERE session_id = $id;";
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<(AgentSession Session, string? ClientType, string? ClientId, DateTimeOffset UpdatedAt)> LoadSessions()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT session_id, subject_id, agent_id, conversation_id, client_type, client_id, updated_at_ms FROM sessions;";
        var result = new List<(AgentSession, string?, string?, DateTimeOffset)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var session = new AgentSession
            {
                SessionId = reader.GetString(0),
                SubjectId = reader.GetString(1),
                AgentId = reader.GetString(2),
                ConversationId = reader.GetString(3),
            };
            result.Add((session,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6))));
        }
        return result;
    }

    // ── 作业 ────────────────────────────────────────────────────

    public void UpsertJob(JobInfo job)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO jobs (id, kind, status, progress, message, session_id, created_at_ms, completed_at_ms)
            VALUES ($id, $kind, $status, $progress, $msg, $sess, $created, $completed)
            ON CONFLICT(id) DO UPDATE SET status = excluded.status, progress = excluded.progress,
              message = excluded.message, completed_at_ms = excluded.completed_at_ms;
            """;
        cmd.Parameters.AddWithValue("$id", job.Id);
        cmd.Parameters.AddWithValue("$kind", job.Kind);
        cmd.Parameters.AddWithValue("$status", job.Status);
        cmd.Parameters.AddWithValue("$progress", job.Progress);
        cmd.Parameters.AddWithValue("$msg", (object?)job.Message ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sess", (object?)job.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", job.CreatedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$completed", (object?)job.CompletedAt?.ToUnixTimeMilliseconds() ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<JobInfo> LoadJobs()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, kind, status, progress, message, session_id, created_at_ms, completed_at_ms FROM jobs;";
        var result = new List<JobInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new JobInfo(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)),
                reader.IsDBNull(7) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7))));
        }
        return result;
    }

    /// <summary>重启恢复：进行中的作业标记为失败（处理器不可续跑）。返回受影响条数。</summary>
    public int MarkRunningJobsFailed(string reason)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE jobs SET status = $failed, message = $reason, completed_at_ms = $now
            WHERE status IN ($queued, $running);
            """;
        cmd.Parameters.AddWithValue("$failed", JobStatusNames.Failed);
        cmd.Parameters.AddWithValue("$queued", JobStatusNames.Queued);
        cmd.Parameters.AddWithValue("$running", JobStatusNames.Running);
        cmd.Parameters.AddWithValue("$reason", reason);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return cmd.ExecuteNonQuery();
    }

    // ── 调度 ────────────────────────────────────────────────────

    public void UpsertSchedule(ScheduleInfo info)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO schedules (id, session_id, kind, at_ms, cron, enabled, next_run_at_ms, payload)
            VALUES ($id, $sess, $kind, $at, $cron, $enabled, $next, $payload)
            ON CONFLICT(id) DO UPDATE SET enabled = excluded.enabled, next_run_at_ms = excluded.next_run_at_ms;
            """;
        cmd.Parameters.AddWithValue("$id", info.Id);
        cmd.Parameters.AddWithValue("$sess", info.SessionId);
        cmd.Parameters.AddWithValue("$kind", info.Kind);
        cmd.Parameters.AddWithValue("$at", (object?)info.At?.ToUnixTimeMilliseconds() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cron", (object?)info.Cron ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enabled", info.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$next", (object?)info.NextRunAt?.ToUnixTimeMilliseconds() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$payload", (object?)info.Payload?.ToJsonString() ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteSchedule(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM schedules WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<ScheduleInfo> LoadSchedules()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, session_id, kind, at_ms, cron, enabled, next_run_at_ms, payload FROM schedules;";
        var result = new List<ScheduleInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ScheduleInfo(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5) == 1,
                reader.IsDBNull(6) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)),
                reader.IsDBNull(7) ? null : JsonNode.Parse(reader.GetString(7))));
        }
        return result;
    }

    // ── 回合轨迹（§5.11 观测/回放） ──────────────────────────────

    private const int TraceRetainDays = 14;
    private const int TraceMaxEvents = 200_000;
    private long _traceEventsSinceTrim;

    public void StartTurn(string turnId, string sessionId, string? origin, DateTimeOffset startedAt)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO traces (turn_id, session_id, origin, started_at_ms)
            VALUES ($id, $sess, $origin, $ts)
            ON CONFLICT(turn_id) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue("$id", turnId);
        cmd.Parameters.AddWithValue("$sess", sessionId);
        cmd.Parameters.AddWithValue("$origin", (object?)origin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", startedAt.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }

    public void AddEvent(string turnId, string type, string? data)
    {
        if (data is { Length: > 4096 })
            data = data[..4096] + "…(truncated)";

        using (var conn = Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO trace_events (turn_id, seq, ts_ms, type, data)
                VALUES ($id, (SELECT COALESCE(MAX(seq), 0) + 1 FROM trace_events WHERE turn_id = $id), $ts, $type, $data);
                """;
            cmd.Parameters.AddWithValue("$id", turnId);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$type", type);
            cmd.Parameters.AddWithValue("$data", (object?)data ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        if (Interlocked.Increment(ref _traceEventsSinceTrim) >= 500)
        {
            Interlocked.Exchange(ref _traceEventsSinceTrim, 0);
            TrimTraces();
        }
    }

    public void CompleteTurn(string turnId, bool success, string? terminationReason, string? finalText)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE traces SET ended_at_ms = $ts, success = $ok, termination_reason = $reason, final_text = $final
            WHERE turn_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", turnId);
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$ok", success ? 1 : 0);
        cmd.Parameters.AddWithValue("$reason", (object?)terminationReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$final", (object?)finalText ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public TurnTraceInfo? Get(string turnId)
    {
        using var conn = Open();

        string sessionId;
        string? origin, reason, finalText;
        long startedAt;
        long? endedAt;
        bool? success;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT session_id, origin, started_at_ms, ended_at_ms, success, termination_reason, final_text FROM traces WHERE turn_id = $id;";
            cmd.Parameters.AddWithValue("$id", turnId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            sessionId = reader.GetString(0);
            origin = reader.IsDBNull(1) ? null : reader.GetString(1);
            startedAt = reader.GetInt64(2);
            endedAt = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            success = reader.IsDBNull(4) ? null : reader.GetInt32(4) == 1;
            reason = reader.IsDBNull(5) ? null : reader.GetString(5);
            finalText = reader.IsDBNull(6) ? null : reader.GetString(6);
        }

        var events = new List<TraceEventInfo>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT seq, ts_ms, type, data FROM trace_events WHERE turn_id = $id ORDER BY seq;";
            cmd.Parameters.AddWithValue("$id", turnId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                events.Add(new TraceEventInfo(
                    reader.GetInt32(0),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        return new TurnTraceInfo(turnId, sessionId, origin,
            DateTimeOffset.FromUnixTimeMilliseconds(startedAt),
            endedAt is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(endedAt.Value),
            success, reason, finalText, events);
    }

    /// <summary>轨迹裁剪：保留 14 天 / 20 万事件（超窗删最旧）。</summary>
    private void TrimTraces()
    {
        using var conn = Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM trace_events WHERE ts_ms < $cutoff;";
            cmd.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-TraceRetainDays).ToUnixTimeMilliseconds());
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM trace_events WHERE rowid IN (
                  SELECT rowid FROM trace_events ORDER BY ts_ms DESC LIMIT -1 OFFSET $max);
                """;
            cmd.Parameters.AddWithValue("$max", TraceMaxEvents);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM traces WHERE turn_id NOT IN (SELECT DISTINCT turn_id FROM trace_events) AND started_at_ms < $cutoff;";
            cmd.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-TraceRetainDays).ToUnixTimeMilliseconds());
            cmd.ExecuteNonQuery();
        }
    }

    // ── 令牌 ────────────────────────────────────────────────────

    public void Save(TokenRecord record)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO tokens (token, client_id, role, expires_at_ms) VALUES ($t, $c, $r, $e)
            ON CONFLICT(token) DO UPDATE SET role = excluded.role, expires_at_ms = excluded.expires_at_ms;
            """;
        cmd.Parameters.AddWithValue("$t", record.Token);
        cmd.Parameters.AddWithValue("$c", record.ClientId);
        cmd.Parameters.AddWithValue("$r", record.Role);
        cmd.Parameters.AddWithValue("$e", record.ExpiresAt.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }

    public void Remove(string token)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tokens WHERE token = $t;";
        cmd.Parameters.AddWithValue("$t", token);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<TokenRecord> LoadAll()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT token, client_id, role, expires_at_ms FROM tokens WHERE expires_at_ms > $now;";
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var result = new List<TokenRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TokenRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3))));
        }
        return result;
    }
}
