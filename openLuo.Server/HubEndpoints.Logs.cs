using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using openLuo.Core.Interfaces;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：日志检索，§5.11）。</summary>
public static partial class HubServer
{
    /// <summary>日志域（admin-only）：热库检索 / 冷文件归档检索 / 时间桶统计。依赖 <see cref="ILogStore"/>。</summary>
    private static void MapLogEndpoints(WebApplication app, HubContext hub)
    {
        var logs = hub.Logs;
        if (logs is null)
            return;

        app.MapGet("/v1/logs", async (string? from, string? to, string? level, string? module, string? category,
            string? keyword, string? sessionId, string? turnId, int? limit, long? beforeId,
            CancellationToken requestCt) =>
        {
            var query = new LogQuery
            {
                From = ParseTime(from),
                To = ParseTime(to),
                MinLevel = level,
                Module = module,
                Category = category,
                Keyword = keyword,
                SessionId = sessionId,
                TurnId = turnId,
                Limit = limit ?? 200,
                BeforeId = beforeId,
            };

            var items = await logs.QueryAsync(query, requestCt);
            return Json(EnvelopeFactory.Create("logs", new LogsResponse
            {
                Items = items.Select(WireMapper.ToDto).ToList(),
                NextBeforeId = items.Count >= Math.Clamp(query.Limit, 1, 1000) ? items[^1].Id : null,
            }));
        });

        app.MapGet("/v1/logs/archive", async (string? date, string? category, string? keyword, string? level,
            int? limit, CancellationToken requestCt) =>
        {
            var items = await logs.SearchArchiveAsync(new ArchiveLogQuery(
                Date: string.IsNullOrWhiteSpace(date) ? "*" : date,
                Category: category,
                Keyword: keyword,
                MinLevel: level,
                Limit: limit ?? 200), requestCt);

            return Json(EnvelopeFactory.Create("logs", new LogsResponse
            {
                Items = items.Select(WireMapper.ToDto).ToList(),
            }));
        });

        app.MapGet("/v1/logs/stats", async (string? from, string? to, string? bucket, CancellationToken requestCt) =>
        {
            var toTs = ParseTime(to) ?? DateTimeOffset.UtcNow;
            var fromTs = ParseTime(from) ?? toTs.AddHours(-1);
            var buckets = await logs.StatsAsync(fromTs, toTs, ParseBucket(bucket), requestCt);
            return Json(EnvelopeFactory.Create("logs", new LogStatsResponse
            {
                Buckets = buckets.Select(b => new LogBucketDto { Start = b.Start, Counts = b.Counts }).ToList(),
            }));
        });
    }
}
