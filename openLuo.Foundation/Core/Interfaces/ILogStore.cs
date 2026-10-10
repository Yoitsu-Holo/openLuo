namespace openLuo.Core.Interfaces;

/// <summary>日志查询条件。时间与标签均可选；<see cref="Keyword"/> 走全文检索；<see cref="BeforeId"/> 用于游标分页。</summary>
public sealed record LogQuery
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    /// <summary>最低级别：debug / info / warn / error（含以上）。</summary>
    public string? MinLevel { get; init; }

    public string? Module { get; init; }
    public string? Category { get; init; }

    /// <summary>关键词（全文检索，命中 <c>msg</c>）。</summary>
    public string? Keyword { get; init; }

    public string? SessionId { get; init; }
    public string? TurnId { get; init; }

    public int Limit { get; init; } = 200;

    /// <summary>只返回 id 小于该值的记录（倒序游标分页）。</summary>
    public long? BeforeId { get; init; }
}

/// <summary>日志记录（热库一行）。</summary>
public sealed record LogRecord(
    long Id,
    DateTimeOffset Ts,
    string Level,
    string Module,
    string Category,
    string? Source,
    string Msg,
    string? Data,
    string? SessionId = null,
    string? TurnId = null);

/// <summary>时间桶计数（按级别）。</summary>
public sealed record LogBucket(DateTimeOffset Start, IReadOnlyDictionary<string, int> Counts);

/// <summary>
/// 日志存储端口（热库）：近期日志可 SQL 热查询 + 时间桶统计；全量日志由实现负责落盘归档。
/// 写入侧由 <c>IGameLogger</c> 驱动（异步、不阻塞业务）；本端口只负责读取。
/// </summary>
public interface ILogStore
{
    /// <summary>按条件查询（时间倒序，游标分页）。</summary>
    Task<IReadOnlyList<LogRecord>> QueryAsync(LogQuery query, CancellationToken ct = default);

    /// <summary>按时间桶统计各级别条数（供面板/趋势）。</summary>
    Task<IReadOnlyList<LogBucket>> StatsAsync(
        DateTimeOffset from, DateTimeOffset to, TimeSpan bucket, CancellationToken ct = default);
}
