using openLuo.Core.Interfaces;
using openLuo.Infrastructure.Logging;
using openLuo.Modules.AppShell.Application;
using Xunit;

namespace openLuo.Logging.Tests;

/// <summary>冷文件（归档）检索：按日期目录扫描 JSONL，支持分类/关键词/最低级别过滤。</summary>
public sealed class ArchiveLogSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "logarchive-tests-" + Guid.NewGuid().ToString("N"));

    public ArchiveLogSearchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void WriteArchive(string date, string category, params string[] lines)
    {
        var directory = Path.Combine(_dir, "core", date);
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, category + ".jsonl"), lines);
    }

    private static string Line(string level, string module, string msg) =>
        $$"""{"ts":"2026-01-02 10:00:00.000","level":"{{level}}","module":"{{module}}","source":"F.cs:1","msg":"{{msg}}"}""";

    private LogStore Create() =>
        new(_dir, new LogHotConfig { Enabled = true }, new LogArchiveConfig { Enabled = true });

    [Fact]
    public async Task Search_FiltersByDateCategoryKeywordAndLevel()
    {
        WriteArchive("20260102", "agent", Line("info", "agent", "normal message"), Line("error", "agent", "needle here"));
        WriteArchive("20260103", "qq", Line("info", "qq", "needle elsewhere"));

        await using var store = Create();

        var all = await store.SearchArchiveAsync(new ArchiveLogQuery());
        Assert.Equal(3, all.Count);

        var byDate = await store.SearchArchiveAsync(new ArchiveLogQuery(Date: "20260102"));
        Assert.Equal(2, byDate.Count);

        var byCategory = await store.SearchArchiveAsync(new ArchiveLogQuery(Date: "*", Category: "qq"));
        Assert.Single(byCategory);
        Assert.Equal("qq", byCategory[0].Category);

        var byKeyword = await store.SearchArchiveAsync(new ArchiveLogQuery(Keyword: "needle"));
        Assert.Equal(2, byKeyword.Count);

        var byLevel = await store.SearchArchiveAsync(new ArchiveLogQuery(MinLevel: "error"));
        Assert.Single(byLevel);
        Assert.Equal("error", byLevel[0].Level);
    }

    [Fact]
    public async Task Search_MissingDirectory_ReturnsEmpty()
    {
        await using var store = Create();
        Assert.Empty(await store.SearchArchiveAsync(new ArchiveLogQuery(Date: "20991231")));
    }
}
