using openLuo.Modules.AppShell.Application;
using openLuo.Infrastructure.Logging;

namespace openLuo.Infrastructure.Tests;

public class GameLoggerTests
{
    [Fact]
    public void Plugin_UsesPluginCategoryOverride()
    {
        var tempDir = CreateTempDir();
        try
        {
            var logger = new GameLogger(tempDir, new LogConfig
            {
                Level = "off",
                Categories = new Dictionary<string, string>
                {
                    ["plugin"] = "debug"
                }
            });

            logger.Plugin("builtin_system_core", "debug", "plugin-visible");

            var file = Path.Combine(tempDir, "plugin", "builtin_system_core.jsonl");
            Assert.True(File.Exists(file));
            Assert.Contains("plugin-visible", File.ReadAllText(file));
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public void Plugin_WithoutOverride_FallsBackToGlobalLevel()
    {
        var tempDir = CreateTempDir();
        try
        {
            var logger = new GameLogger(tempDir, new LogConfig
            {
                Level = "off"
            });

            logger.Plugin("builtin_system_core", "debug", "plugin-hidden");

            var file = Path.Combine(tempDir, "plugin", "builtin_system_core.jsonl");
            Assert.False(File.Exists(file));
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gimai_logger_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
