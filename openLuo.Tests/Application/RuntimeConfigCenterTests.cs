using openLuo.Modules.AppShell.Application;

namespace openLuo.Application.Tests;

public sealed class RuntimeConfigCenterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"gimai-config-{Guid.NewGuid():N}");

    public RuntimeConfigCenterTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task Reload_KeepsRestartRequiredSettingsAndAppliesHotSettings()
    {
        var configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(configDir);

        await File.WriteAllTextAsync(Path.Combine(configDir, "world.jsonc"),
            """
            {
              "databasePath": "db-a.sqlite",
              "sqliteVec": {
                "extensionPath": "ext-a",
                "vectorDimensions": 2560
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(configDir, "llm.jsonc"),
            """
            {
              "llm": {
                "apiKey": "key-a",
                "timeoutSeconds": 10
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(configDir, "log.jsonc"),
            """
            {
              "log": {
                "level": "info"
              }
            }
            """);

        using var center = new RuntimeConfigCenter(configDir);

        // Hot-reload: update llm.json
        await File.WriteAllTextAsync(Path.Combine(configDir, "llm.jsonc"),
            """
            {
              "llm": {
                "apiKey": "key-b",
                "timeoutSeconds": 42
              }
            }
            """);

        // Hot-reload: update log.json
        await File.WriteAllTextAsync(Path.Combine(configDir, "log.jsonc"),
            """
            {
              "log": {
                "level": "debug"
              }
            }
            """);

        // Hot-reload: attempt to change restart-required settings
        await File.WriteAllTextAsync(Path.Combine(configDir, "world.jsonc"),
            """
            {
              "databasePath": "db-b.sqlite",
              "sqliteVec": {
                "extensionPath": "ext-b",
                "vectorDimensions": 1024
              }
            }
            """);

        await WaitUntilAsync(() =>
        {
            var snapshot = center.GetSnapshot();
            return snapshot.Log.Level == "debug" && snapshot.Llm.TimeoutSeconds == 42;
        });

        var reloaded = center.GetSnapshot();
        Assert.Equal("debug", reloaded.Log.Level);
        Assert.Equal(42, reloaded.Llm.TimeoutSeconds);
        Assert.Equal("key-b", reloaded.Llm.ApiKey);
        Assert.Equal("db-a.sqlite", reloaded.DatabasePath);
        Assert.Equal("ext-a", reloaded.SqliteVec.ExtensionPath);
        Assert.Equal(2560, reloaded.SqliteVec.VectorDimensions);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 20; i++)
        {
            if (predicate())
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException("Config center did not reload within the expected time.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
