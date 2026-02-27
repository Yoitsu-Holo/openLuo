using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace openLuo.Modules.AppShell.Application;

/// <summary>
/// Loads partial AppConfig from categorized JSON files in a directory.
/// Each file deserializes to AppConfig; only non-default values are merged.
///
/// Files loaded:
///   {name}.jsonc — user configuration overrides
///
/// C# class property defaults serve as the ultimate fallback.
///
/// NOTE: {name}.example.jsonc files are NOT loaded at runtime.
/// They are templates for users to copy and customize.
/// </summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppConfig LoadDirectory(string configDir, ILogger? logger = null)
    {
        Directory.CreateDirectory(configDir);
        logger?.LogInformation("开始加载配置目录: {ConfigDir}", configDir);

        var merged = new AppConfig();

        // Load user config files
        foreach (var runtimePath in GetRuntimeConfigFiles(configDir))
        {
            var partial = LoadPartial(runtimePath, logger);
            if (partial is not null)
            {
                logger?.LogInformation("已加载运行时配置: {FileName}", Path.GetFileName(runtimePath));
                merged = Merge(merged, partial);
            }
        }

        logger?.LogInformation("配置加载完成");
        merged.Normalize();
        return merged;
    }

    private static IEnumerable<string> GetRuntimeConfigFiles(string configDir)
    {
        return Directory.GetFiles(configDir, "*.jsonc", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !name.EndsWith(".example.jsonc", StringComparison.OrdinalIgnoreCase)
                       && !name.StartsWith('.')
                       && !name.EndsWith('~');
            });
    }

    private static AppConfig? LoadPartial(string path, ILogger? logger = null)
    {
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                logger?.LogInformation("跳过空配置文件: {FileName}", Path.GetFileName(path));
                return null;
            }
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "加载配置文件失败: {FileName}", Path.GetFileName(path));
            return null;
        }
    }

    /// <summary>
    /// Merge two AppConfigs. For each sub-section, non-default values from the override
    /// take precedence. A value is considered "default" if its sub-section matches a fresh instance.
    /// </summary>
    private static AppConfig Merge(AppConfig baseline, AppConfig? overrideConfig)
    {
        if (overrideConfig is null)
            return baseline.Clone();

        var result = baseline.Clone();
        var defaultCfg = new AppConfig();

        if (!AreEqual(overrideConfig.Llm, defaultCfg.Llm))
            result.Llm = overrideConfig.Llm.Clone();
        if (!AreEqual(overrideConfig.Embedding, defaultCfg.Embedding))
            result.Embedding = overrideConfig.Embedding.Clone();
        if (!string.IsNullOrWhiteSpace(overrideConfig.DatabasePath) && overrideConfig.DatabasePath != defaultCfg.DatabasePath)
            result.DatabasePath = overrideConfig.DatabasePath;
        if (!AreEqual(overrideConfig.Agent, defaultCfg.Agent))
            result.Agent = overrideConfig.Agent.Clone();
        if (!AreEqual(overrideConfig.Security, defaultCfg.Security))
            result.Security = overrideConfig.Security.Clone();
        if (!AreEqual(overrideConfig.Timeouts, defaultCfg.Timeouts))
            result.Timeouts = overrideConfig.Timeouts.Clone();
        if (!AreEqual(overrideConfig.Resilience, defaultCfg.Resilience))
            result.Resilience = overrideConfig.Resilience.Clone();
        if (!AreEqual(overrideConfig.Lifecycle, defaultCfg.Lifecycle))
            result.Lifecycle = overrideConfig.Lifecycle.Clone();
        if (!AreEqual(overrideConfig.Log, defaultCfg.Log))
            result.Log = overrideConfig.Log.Clone();
        if (!AreEqual(overrideConfig.MemoryRetrieval, defaultCfg.MemoryRetrieval))
            result.MemoryRetrieval = overrideConfig.MemoryRetrieval.Clone();
        if (!AreEqual(overrideConfig.MemoryStore, defaultCfg.MemoryStore))
            result.MemoryStore = overrideConfig.MemoryStore.Clone();
        if (!AreEqual(overrideConfig.InterAgent, defaultCfg.InterAgent))
            result.InterAgent = overrideConfig.InterAgent.Clone();
        if (!AreEqual(overrideConfig.PluginRuntime, defaultCfg.PluginRuntime))
            result.PluginRuntime = overrideConfig.PluginRuntime.Clone();
        if (!AreEqual(overrideConfig.SqliteVec, defaultCfg.SqliteVec))
            result.SqliteVec = overrideConfig.SqliteVec.Clone();
        if (!AreEqual(overrideConfig.Executors, defaultCfg.Executors))
            result.Executors = overrideConfig.Executors.Clone();

        return result;
    }

    private static bool AreEqual<T>(T? a, T? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        var jsonA = JsonSerializer.Serialize(a, JsonOptions);
        var jsonB = JsonSerializer.Serialize(b, JsonOptions);
        return jsonA == jsonB;
    }
}
