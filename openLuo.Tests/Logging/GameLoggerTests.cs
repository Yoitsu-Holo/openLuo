using System.Text;
using System.Text.Json;
using openLuo.Core.Interfaces;
using openLuo.Infrastructure.Logging;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Tests.Logging;

public sealed class GameLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "openluo-logtest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private (GameLogger logger, MemoryStream error) CreateLogger(bool outputToConsole = true)
    {
        var error = new MemoryStream();
        var streams = Substitute.For<IGameStreams>();
        streams.Error.Returns(error);
        var logger = new GameLogger(_dir, new LogConfig { Level = "info", OutputToConsole = outputToConsole }, streams);
        return (logger, error);
    }

    [Fact]
    public void ConsoleOutput_IsMetaLineThenContentLine_WithTsMatchingFileEntry()
    {
        var (logger, error) = CreateLogger();
        logger.Info("qqbot", "hello world", new { status = "ok", ms = 42 });

        var console = Encoding.UTF8.GetString(error.ToArray());
        var fileLine = File.ReadAllLines(Path.Combine(_dir, "core", "qqbot.jsonl")).Single();

        // 终端：1+N 行——元信息行 [ts] [level] [source] [module]，内容行随其后
        Assert.Matches(
            @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[\x1b\[32minfo\x1b\[0m\] \[GameLoggerTests\.cs:\d+\] \[qqbot\]\nhello world \{\""status\"":\""ok\"",\""ms\"":42\}\n$",
            console);

        // 文件 JSON 的 ts 与终端元信息行逐字符一致
        var fileTs = JsonDocument.Parse(fileLine).RootElement.GetProperty("ts").GetString();
        Assert.StartsWith("[" + fileTs + "] ", console);

        // 文件 JSON 同步 module/source 字段
        var json = JsonDocument.Parse(fileLine).RootElement;
        Assert.Equal("qqbot", json.GetProperty("module").GetString());
        Assert.Matches(@"^GameLoggerTests\.cs:\d+$", json.GetProperty("source").GetString());
        Assert.Equal("ok", json.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(42, json.GetProperty("data").GetProperty("ms").GetInt32());
    }

    [Fact]
    public void ConsoleOutput_OmitsDataSuffix_WhenDataNull()
    {
        var (logger, error) = CreateLogger();
        logger.Info("qqbot", "plain message");

        var console = Encoding.UTF8.GetString(error.ToArray());
        Assert.Matches(
            @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[\x1b\[32minfo\x1b\[0m\] \[GameLoggerTests\.cs:\d+\] \[qqbot\]\nplain message\n$",
            console);

        // 文件侧无 data 字段
        var fileLine = File.ReadAllLines(Path.Combine(_dir, "core", "qqbot.jsonl")).Single();
        Assert.False(JsonDocument.Parse(fileLine).RootElement.TryGetProperty("data", out _));
    }

    [Fact]
    public void NoConsoleOutput_WhenOutputToConsoleDisabled_AndModuleDerivedFromCategory()
    {
        var (logger, error) = CreateLogger(outputToConsole: false);
        logger.Warn("agent/dispatch", "batch rejected");

        Assert.Equal(0, error.Length);
        // 文件仍照常写入；module 从 category 派生（agent/dispatch → agent）
        var fileLine = File.ReadAllLines(Path.Combine(_dir, "core", "agent-dispatch.jsonl")).Single();
        var json = JsonDocument.Parse(fileLine).RootElement;
        Assert.Equal("batch rejected", json.GetProperty("msg").GetString());
        Assert.Equal("agent", json.GetProperty("module").GetString());
        Assert.Equal("warn", json.GetProperty("level").GetString());
    }
}
