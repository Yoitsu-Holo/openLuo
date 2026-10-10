using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Infrastructure.Logging;

/// <summary>
/// 日志存储（热库 + 冷文件）：
/// <list type="bullet">
///   <item><b>热库</b>：SQLite（WAL，独立于业务库），近期日志可 SQL / 全文热查询，超窗裁剪（天数 + 行数）。</item>
///   <item><b>冷文件</b>：全量 JSONL 按天分目录永久落盘（<c>{logDir}/{archive.Dir}/{yyyyMMdd}/{category}.jsonl</c>）。</item>
///   <item><b>写入</b>：有界异步队列（绝不阻塞业务，满则丢弃并计数）+ 单写者批量事务。</item>
/// </list>
/// </summary>
public sealed class LogStore : ILogStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _logDir;
    private readonly LogHotConfig _hot;
    private readonly LogArchiveConfig _archive;
    private readonly string _dbPath;
    private readonly Channel<LogRecord> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writer;

    private readonly ConcurrentDictionary<string, StreamWriter> _fileWriters = new(StringComparer.Ordinal);
    private long _dropped;
    private long _entriesSinceTrim;

    /// <summary>0=未释放；1=已释放（DisposeAsync 幂等，见其注释）。</summary>
    private int _disposed;

    public LogStore(string logDir, LogHotConfig? hot = null, LogArchiveConfig? archive = null)
    {
        _logDir = logDir;
        _hot = hot?.Clone() ?? new LogHotConfig();
        _archive = archive?.Clone() ?? new LogArchiveConfig();
        _dbPath = Path.Combine(logDir, _hot.Path);

        Directory.CreateDirectory(logDir);
        if (_hot.Enabled)
            InitializeHotDb();

        _queue = Channel.CreateBounded<LogRecord>(new BoundedChannelOptions(8192)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,   // 日志绝不阻塞业务；满则丢弃
        });

        _writer = Task.Run(() => RunWriterAsync(_cts.Token));
    }

    /// <summary>入队一条日志（Id 由热库分配，写入侧传 0）。队列满时丢弃并计数。</summary>
    public void Enqueue(LogRecord record)
    {
        if (!_queue.Writer.TryWrite(record))
            Interlocked.Increment(ref _dropped);
    }

    /// <summary>被丢弃的日志条数（队列满）。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    // ── 热库初始化 ──────────────────────────────────────────────

    private void InitializeHotDb()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        Execute(conn, "PRAGMA journal_mode=WAL;");
        Execute(conn, "PRAGMA auto_vacuum=INCREMENTAL;");
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS logs (
              id INTEGER PRIMARY KEY,
              ts_ms INTEGER NOT NULL,
              level INTEGER NOT NULL,
              level_name TEXT NOT NULL,
              module TEXT NOT NULL,
              category TEXT NOT NULL,
              source TEXT,
              msg TEXT NOT NULL,
              data TEXT,
              session_id TEXT,
              turn_id TEXT
            );
            """);
        Execute(conn, "CREATE INDEX IF NOT EXISTS ix_logs_ts ON logs(ts_ms DESC);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS ix_logs_lvl_ts ON logs(level, ts_ms DESC);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS ix_logs_mod_ts ON logs(module, ts_ms DESC);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS ix_logs_sess_ts ON logs(session_id, ts_ms DESC);");
        // 全文检索（FTS5 外部内容表 + 触发器同步；e_sqlite3 已含 ENABLE_FTS5）
        Execute(conn, "CREATE VIRTUAL TABLE IF NOT EXISTS logs_fts USING fts5(msg, content='logs', content_rowid='id', tokenize='unicode61');");
        Execute(conn, """
            CREATE TRIGGER IF NOT EXISTS logs_ai AFTER INSERT ON logs BEGIN
              INSERT INTO logs_fts(rowid, msg) VALUES (new.id, new.msg);
            END;
            """);
        Execute(conn, """
            CREATE TRIGGER IF NOT EXISTS logs_ad AFTER DELETE ON logs BEGIN
              INSERT INTO logs_fts(logs_fts, rowid, msg) VALUES ('delete', old.id, old.msg);
            END;
            """);
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ── 写入循环 ────────────────────────────────────────────────

    private async Task RunWriterAsync(CancellationToken ct)
    {
        SqliteConnection? conn = null;
        try
        {
            conn = _hot.Enabled ? new SqliteConnection($"Data Source={_dbPath}") : null;
            conn?.Open();

            var batch = new List<LogRecord>(_hot.BatchSize);
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Count < _hot.BatchSize && _queue.Reader.TryRead(out var record))
                    batch.Add(record);

                if (batch.Count == 0)
                    continue;

                try
                {
                    if (conn is not null)
                        InsertBatch(conn, batch);
                    WriteColdFiles(batch);

                    if (conn is not null)
                    {
                        // 裁剪触发：累计新增条数达到阈值（上限钳制，保证热库规模上界 ≈ MaxRows + 阈值）
                        _entriesSinceTrim += batch.Count;
                        var threshold = Math.Clamp(_hot.MaxRows, 50, 50_000);
                        if (_entriesSinceTrim >= threshold)
                        {
                            _entriesSinceTrim = 0;
                            Trim(conn);
                        }
                    }
                }
                catch (Exception)
                {
                    // 日志写入失败不得影响业务
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        finally
        {
            conn?.Dispose();
            foreach (var writer in _fileWriters.Values)
            {
                try { writer.Dispose(); } catch { }
            }
        }
    }

    private static void InsertBatch(SqliteConnection conn, List<LogRecord> batch)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO logs (ts_ms, level, level_name, module, category, source, msg, data, session_id, turn_id)
            VALUES ($ts, $lv, $lvn, $mod, $cat, $src, $msg, $data, $sess, $turn);
            """;
        var pTs = cmd.Parameters.Add("$ts", SqliteType.Integer);
        var pLv = cmd.Parameters.Add("$lv", SqliteType.Integer);
        var pLvn = cmd.Parameters.Add("$lvn", SqliteType.Text);
        var pMod = cmd.Parameters.Add("$mod", SqliteType.Text);
        var pCat = cmd.Parameters.Add("$cat", SqliteType.Text);
        var pSrc = cmd.Parameters.Add("$src", SqliteType.Text);
        var pMsg = cmd.Parameters.Add("$msg", SqliteType.Text);
        var pData = cmd.Parameters.Add("$data", SqliteType.Text);
        var pSess = cmd.Parameters.Add("$sess", SqliteType.Text);
        var pTurn = cmd.Parameters.Add("$turn", SqliteType.Text);

        foreach (var record in batch)
        {
            pTs.Value = record.Ts.ToUnixTimeMilliseconds();
            pLv.Value = LevelRank(record.Level);
            pLvn.Value = record.Level;
            pMod.Value = record.Module;
            pCat.Value = record.Category;
            pSrc.Value = (object?)record.Source ?? DBNull.Value;
            pMsg.Value = record.Msg;
            pData.Value = (object?)Truncate(record.Data) ?? DBNull.Value;
            pSess.Value = (object?)record.SessionId ?? DBNull.Value;
            pTurn.Value = (object?)record.TurnId ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static string? Truncate(string? data) =>
        data is null || data.Length <= 4096 ? data : data[..4096] + "…(truncated)";

    private void WriteColdFiles(List<LogRecord> batch)
    {
        if (!_archive.Enabled)
            return;

        var day = DateTime.Now.ToString("yyyyMMdd");
        foreach (var record in batch)
        {
            var path = Path.Combine(_logDir, _archive.Dir, day, Sanitize(record.Category) + ".jsonl");
            var writer = _fileWriters.GetOrAdd(path, p =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                return new StreamWriter(new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
            });

            var entry = record.Data is null
                ? JsonSerializer.Serialize(new { ts = record.Ts.ToString("yyyy-MM-dd HH:mm:ss.fff"), level = record.Level, module = record.Module, source = record.Source, msg = record.Msg }, Json)
                : JsonSerializer.Serialize(new { ts = record.Ts.ToString("yyyy-MM-dd HH:mm:ss.fff"), level = record.Level, module = record.Module, source = record.Source, msg = record.Msg, data = record.Data }, Json);

            writer.WriteLine(entry);
        }

        foreach (var writer in _fileWriters.Values)
        {
            try { writer.Flush(); } catch { }
        }
    }

    /// <summary>裁剪热库：按保留天数 + 最大行数（LRU 语义，删最旧）。</summary>
    private void Trim(SqliteConnection conn)
    {
        if (_hot.RetainDays > 0)
        {
            using var del = conn.CreateCommand();
            del.CommandText = "DELETE FROM logs WHERE ts_ms < $cutoff;";
            del.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-_hot.RetainDays).ToUnixTimeMilliseconds());
            del.ExecuteNonQuery();
        }

        if (_hot.MaxRows > 0)
        {
            using var del = conn.CreateCommand();
            del.CommandText = "DELETE FROM logs WHERE id IN (SELECT id FROM logs ORDER BY id DESC LIMIT -1 OFFSET $max);";
            del.Parameters.AddWithValue("$max", _hot.MaxRows);
            del.ExecuteNonQuery();
        }

        using var vacuum = conn.CreateCommand();
        vacuum.CommandText = "PRAGMA incremental_vacuum;";
        vacuum.ExecuteNonQuery();

        // FTS5 索引优化：合并段、回收未引用空间（在裁剪后执行）
        try
        {
            using var optimize = conn.CreateCommand();
            optimize.CommandText = "INSERT INTO logs_fts(logs_fts) VALUES('optimize');";
            optimize.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // 旧库缺少 FTS 表时忽略
        }
    }

    // ── 查询 ────────────────────────────────────────────────────

    public Task<IReadOnlyList<LogRecord>> QueryAsync(LogQuery query, CancellationToken ct = default)
    {
        var limit = Math.Clamp(query.Limit, 1, 1000);

        using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        conn.Open();

        using var cmd = conn.CreateCommand();
        var where = new List<string>();

        if (query.Keyword is { Length: > 0 })
        {
            cmd.CommandText = "SELECT l.id, l.ts_ms, l.level_name, l.module, l.category, l.source, l.msg, l.data, l.session_id, l.turn_id " +
                              "FROM logs l JOIN logs_fts f ON f.rowid = l.id WHERE logs_fts MATCH $kw";
            cmd.Parameters.AddWithValue("$kw", ToFtsPhrase(query.Keyword));
        }
        else
        {
            cmd.CommandText = "SELECT id, ts_ms, level_name, module, category, source, msg, data, session_id, turn_id FROM logs WHERE 1=1";
        }

        if (query.From is { } from) { where.Add("ts_ms >= $from"); cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds()); }
        if (query.To is { } to) { where.Add("ts_ms <= $to"); cmd.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds()); }
        if (!string.IsNullOrWhiteSpace(query.MinLevel))
        {
            where.Add("level >= $minLv");
            cmd.Parameters.AddWithValue("$minLv", LevelRank(query.MinLevel));
        }
        if (!string.IsNullOrWhiteSpace(query.Module)) { where.Add("module = $mod"); cmd.Parameters.AddWithValue("$mod", query.Module); }
        if (!string.IsNullOrWhiteSpace(query.Category)) { where.Add("category = $cat"); cmd.Parameters.AddWithValue("$cat", query.Category); }
        if (!string.IsNullOrWhiteSpace(query.SessionId)) { where.Add("session_id = $sess"); cmd.Parameters.AddWithValue("$sess", query.SessionId); }
        if (!string.IsNullOrWhiteSpace(query.TurnId)) { where.Add("turn_id = $turn"); cmd.Parameters.AddWithValue("$turn", query.TurnId); }
        if (query.BeforeId is { } before) { where.Add("id < $before"); cmd.Parameters.AddWithValue("$before", before); }

        if (where.Count > 0)
            cmd.CommandText += " AND " + string.Join(" AND ", where);

        cmd.CommandText += " ORDER BY id DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<LogRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new LogRecord(
                Id: reader.GetInt64(0),
                Ts: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                Level: reader.GetString(2),
                Module: reader.GetString(3),
                Category: reader.GetString(4),
                Source: reader.IsDBNull(5) ? null : reader.GetString(5),
                Msg: reader.GetString(6),
                Data: reader.IsDBNull(7) ? null : reader.GetString(7),
                SessionId: reader.IsDBNull(8) ? null : reader.GetString(8),
                TurnId: reader.IsDBNull(9) ? null : reader.GetString(9)));
        }
        return Task.FromResult<IReadOnlyList<LogRecord>>(result);
    }

    public Task<IReadOnlyList<LogBucket>> StatsAsync(
        DateTimeOffset from, DateTimeOffset to, TimeSpan bucket, CancellationToken ct = default)
    {
        var bucketMs = Math.Max(1000, (long)bucket.TotalMilliseconds);

        using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT (ts_ms - $from) / $bucket AS slot, level_name, COUNT(*)
            FROM logs
            WHERE ts_ms >= $from AND ts_ms <= $to
            GROUP BY slot, level_name
            ORDER BY slot;
            """;
        cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$bucket", bucketMs);

        var map = new Dictionary<long, Dictionary<string, int>>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var slot = reader.GetInt64(0);
                var level = reader.GetString(1);
                var count = reader.GetInt32(2);
                if (!map.TryGetValue(slot, out var counts))
                    map[slot] = counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                counts[level] = count;
            }
        }

        var start = DateTimeOffset.FromUnixTimeMilliseconds(from.ToUnixTimeMilliseconds());
        var buckets = map
            .OrderBy(kv => kv.Key)
            .Select(kv => new LogBucket(start.AddMilliseconds(kv.Key * bucketMs), kv.Value))
            .ToList();
        return Task.FromResult<IReadOnlyList<LogBucket>>(buckets);
    }

    /// <summary>检索冷文件：扫描 <c>{logDir}/{archive.Dir}/{date}/{category}.jsonl</c>（date 为 <c>*</c> 时全部日期）。</summary>
    public Task<IReadOnlyList<LogRecord>> SearchArchiveAsync(ArchiveLogQuery query, CancellationToken ct = default)
    {
        var results = new List<LogRecord>();
        if (!_archive.Enabled)
            return Task.FromResult<IReadOnlyList<LogRecord>>(results);

        var root = Path.Combine(_logDir, _archive.Dir);
        if (!Directory.Exists(root))
            return Task.FromResult<IReadOnlyList<LogRecord>>(results);

        var dateDirs = query.Date is "*"
            ? Directory.GetDirectories(root)
            : [Path.Combine(root, query.Date)];

        var limit = Math.Clamp(query.Limit, 1, 2000);
        var minRank = string.IsNullOrWhiteSpace(query.MinLevel) ? 0 : LevelRank(query.MinLevel);

        foreach (var directory in dateDirs.OrderByDescending(d => d))
        {
            if (!Directory.Exists(directory))
                continue;

            var files = query.Category is { Length: > 0 }
                ? [Path.Combine(directory, Sanitize(query.Category) + ".jsonl")]
                : Directory.GetFiles(directory, "*.jsonl");

            foreach (var file in files)
            {
                if (!File.Exists(file))
                    continue;

                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    LogRecord? record;
                    try
                    {
                        var node = JsonNode.Parse(line);
                        if (node is null)
                            continue;

                        var level = node["level"]?.GetValue<string>() ?? "info";
                        if (minRank > 0 && LevelRank(level) < minRank)
                            continue;

                        var msg = node["msg"]?.GetValue<string>() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(query.Keyword)
                            && !msg.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var ts = DateTimeOffset.TryParse(node["ts"]?.GetValue<string>(), out var parsed)
                            ? parsed
                            : DateTimeOffset.MinValue;

                        record = new LogRecord(
                            Id: 0, Ts: ts, Level: level,
                            Module: node["module"]?.GetValue<string>() ?? string.Empty,
                            Category: Path.GetFileNameWithoutExtension(file),
                            Source: node["source"]?.GetValue<string>(),
                            Msg: msg,
                            Data: node["data"]?.ToJsonString());
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    results.Add(record);

                    if (results.Count >= limit)
                    {
                        results.Sort((a, b) => b.Ts.CompareTo(a.Ts));
                        return Task.FromResult<IReadOnlyList<LogRecord>>(results);
                    }
                }
            }
        }

        results.Sort((a, b) => b.Ts.CompareTo(a.Ts));
        return Task.FromResult<IReadOnlyList<LogRecord>>(results);
    }

    /// <summary>FTS5 查询词 → 短语（转义引号，避免语法注入）。</summary>
    private static string ToFtsPhrase(string keyword) => "\"" + keyword.Replace("\"", "\"\"") + "\"";

    public static int LevelRank(string level) => level.ToLowerInvariant() switch
    {
        "debug" or "trace" => 1,
        "info" => 2,
        "warn" or "warning" => 3,
        "error" or "fatal" or "critical" => 4,
        _ => 2,
    };

    private static string Sanitize(string value) => value.Replace('/', '-').Replace('\\', '-');

    /// <summary>
    /// 幂等释放。**必须幂等**：容器中 LogStore 与 ILogStore 两个描述符指向同一实例，释放阶段会对本实例调用两次；
    /// 第二次若再触碰已释放的 <see cref="_cts"/>，会抛 <see cref="ObjectDisposedException"/>，
    /// 该异常从 Main 冒泡出去会让进程 abort（Ctrl+C 关闭时表现为 core dumped）。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _queue.Writer.TryComplete();
        try { await _writer.ConfigureAwait(false); } catch { }
        try { await _cts.CancelAsync().ConfigureAwait(false); } catch (ObjectDisposedException) { }
        _cts.Dispose();
    }
}
