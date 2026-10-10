using openLuo.Core.Interfaces;
using openLuo.Infrastructure.Logging;
using openLuo.Modules.AppShell.Application;
using Xunit;

namespace openLuo.Logging.Tests;

/// <summary>
/// 日志存储（热库 + 冷文件）：过滤 / 全文检索 / 游标分页 / 时间桶统计 / 冷文件落盘 / 保留裁剪。
/// </summary>
public sealed class LogStoreTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "logstore-tests-" + Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    private LogStore Create(int maxRows = 100_000, int batchSize = 20) =>
        new(_dir, new LogHotConfig { BatchSize = batchSize, FlushMs = 0, MaxRows = maxRows }, new LogArchiveConfig());

    private static LogRecord Record(string level, string module, string category, string msg,
        string? sessionId = null, string? turnId = null) =>
        new(0, DateTimeOffset.Now, level, module, category, "File.cs:1", msg, null, sessionId, turnId);

    /// <summary>等待写入管道把条目刷入热库（轮询，避免依赖固定 sleep）。</summary>
    private static async Task WaitForAsync(ILogStore store, Func<Task<int>> count, int expected, int timeoutMs = 4000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (await count() >= expected)
                return;
            await Task.Delay(50);
        }
        Assert.Fail($"timeout waiting for {expected} entries");
    }

    [Fact]
    public async Task Query_FiltersByLevel_Module_And_Session()
    {
        await using var store = Create();
        for (var i = 0; i < 30; i++)
        {
            var level = i % 10 == 0 ? "error" : "info";
            var module = i % 2 == 0 ? "agent" : "qq";
            store.Enqueue(Record(level, module, $"{module}/x", $"m{i}", i % 3 == 0 ? "s1" : null));
        }

        await WaitForAsync(store, async () => (await store.QueryAsync(new LogQuery { Limit = 500 })).Count, 30);

        Assert.Equal(30, (await store.QueryAsync(new LogQuery { Limit = 500 })).Count);
        Assert.Equal(3, (await store.QueryAsync(new LogQuery { MinLevel = "error" })).Count);
        Assert.Equal(15, (await store.QueryAsync(new LogQuery { Module = "agent" })).Count);
        Assert.Equal(10, (await store.QueryAsync(new LogQuery { SessionId = "s1" })).Count);
    }

    [Fact]
    public async Task Query_Keyword_UsesFullTextSearch()
    {
        await using var store = Create();
        store.Enqueue(Record("info", "agent", "agent/x", "普通消息"));
        store.Enqueue(Record("error", "agent", "agent/x", "needle in haystack"));
        await WaitForAsync(store, async () => (await store.QueryAsync(new LogQuery { Limit = 100 })).Count, 2);

        var hit = await store.QueryAsync(new LogQuery { Keyword = "needle" });
        Assert.Single(hit);
        Assert.Contains("needle", hit[0].Msg);

        Assert.Empty(await store.QueryAsync(new LogQuery { Keyword = "not-present" }));
    }

    [Fact]
    public async Task Query_SupportsCursorPaging()
    {
        await using var store = Create();
        for (var i = 0; i < 25; i++)
            store.Enqueue(Record("info", "agent", "agent/x", $"m{i}"));
        await WaitForAsync(store, async () => (await store.QueryAsync(new LogQuery { Limit = 500 })).Count, 25);

        var page1 = await store.QueryAsync(new LogQuery { Limit = 10 });
        Assert.Equal(10, page1.Count);

        var page2 = await store.QueryAsync(new LogQuery { Limit = 10, BeforeId = page1[^1].Id });
        Assert.Equal(10, page2.Count);
        Assert.True(page2[0].Id < page1[^1].Id, "游标分页应严格递减");

        Assert.Empty(page1.Select(r => r.Id).Intersect(page2.Select(r => r.Id)));
    }

    [Fact]
    public async Task Stats_BucketsCountsByLevel()
    {
        await using var store = Create();
        for (var i = 0; i < 12; i++)
            store.Enqueue(Record(i % 4 == 0 ? "error" : "info", "agent", "agent/x", $"m{i}"));
        await WaitForAsync(store, async () => (await store.QueryAsync(new LogQuery { Limit = 500 })).Count, 12);

        var buckets = await store.StatsAsync(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromMinutes(1));

        Assert.NotEmpty(buckets);
        var totals = buckets.SelectMany(b => b.Counts).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
        Assert.Equal(9, totals["info"]);
        Assert.Equal(3, totals["error"]);
    }

    [Fact]
    public async Task ColdFiles_AreWritten_PerCategoryInDateFolder()
    {
        await using var store = Create();
        store.Enqueue(Record("info", "agent", "agent/dispatch", "cold-1"));
        store.Enqueue(Record("info", "agent", "agent/dispatch", "cold-2"));
        store.Enqueue(Record("warn", "qq", "qq", "cold-3"));
        await WaitForAsync(store, async () => (await store.QueryAsync(new LogQuery { Limit = 100 })).Count, 3);

        var files = Array.Empty<string>();
        var deadline = Environment.TickCount64 + 4000;
        while (Environment.TickCount64 < deadline)
        {
            files = Directory.GetFiles(Path.Combine(_dir, "core"), "*.jsonl", SearchOption.AllDirectories);
            if (files.Length >= 2)
                break;
            await Task.Delay(50);
        }

        Assert.Equal(2, files.Length);

        var dispatch = files.Single(f => f.EndsWith("agent-dispatch.jsonl", StringComparison.Ordinal));
        var lines = await File.ReadAllLinesAsync(dispatch);
        Assert.Equal(2, lines.Length);
        Assert.Contains("cold-1", lines[0]);
    }

    [Fact]
    public async Task Retention_TrimsToMaxRows()
    {
        await using var store = Create(maxRows: 10, batchSize: 5);
        for (var i = 0; i < 300; i++)
            store.Enqueue(Record("info", "trim", "trim/x", $"r{i}"));

        var deadline = Environment.TickCount64 + 6000;
        int remaining;
        do
        {
            await Task.Delay(100);
            remaining = (await store.QueryAsync(new LogQuery { Limit = 5000 })).Count;
            // 裁剪阈值 = clamp(MaxRows,50,50k) = 50 → 稳态应回落
        } while (remaining > 60 && Environment.TickCount64 < deadline);

        Assert.InRange(remaining, 1, 60);
    }
}
