using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace openLuo.Modules.AppShell.Application;

public interface IRuntimeConfigCenter
{
    string ConfigPath { get; }
    AppConfig GetSnapshot();
}

public sealed class RuntimeConfigCenter : IRuntimeConfigCenter, IDisposable
{
    private readonly string _configDir;
    private readonly FileSystemWatcher _watcher;
    private readonly object _reloadGate = new();
    private readonly Timer _reloadTimer;
    private volatile AppConfig _snapshot;
    private readonly ILogger? _logger;

    public RuntimeConfigCenter(string configDir, ILogger? logger = null)
    {
        _configDir = configDir;
        _logger = logger;
        _snapshot = ConfigLoader.LoadDirectory(configDir, _logger);

        _reloadTimer = new Timer(_ => ReloadFromDisk(), null, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(_configDir, "*.jsonc")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnConfigFileChanged;
        _watcher.Created += OnConfigFileChanged;
        _watcher.Renamed += OnConfigFileChanged;
        _watcher.Deleted += OnConfigFileChanged;
    }

    public string ConfigPath => _configDir;

    public AppConfig GetSnapshot() => _snapshot.Clone();

    private void OnConfigFileChanged(object? sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.Name);
        if (name.StartsWith('.') || name.EndsWith('~'))
            return;

        lock (_reloadGate)
            _reloadTimer.Change(250, Timeout.Infinite);
    }

    private void ReloadFromDisk()
    {
        try
        {
            var loaded = ConfigLoader.LoadDirectory(_configDir, _logger);
            var current = _snapshot;
            var next = ApplyReloadRules(current, loaded, out var deferredPaths);
            _snapshot = next;

            if (deferredPaths.Count > 0)
            {
                _logger?.LogWarning(
                    "检测到需重启才能生效的配置变更，已忽略热更新: {DeferredPaths}",
                    string.Join(", ", deferredPaths));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "配置热加载失败，继续使用当前快照");
        }
    }

    private static AppConfig ApplyReloadRules(AppConfig current, AppConfig loaded, out List<string> deferredPaths)
    {
        deferredPaths = [];
        var next = loaded.Clone();

        if (!string.Equals(current.DatabasePath, loaded.DatabasePath, StringComparison.Ordinal))
        {
            next.DatabasePath = current.DatabasePath;
            deferredPaths.Add("databasePath");
        }

        if (!string.Equals(current.SqliteVec.ExtensionPath, loaded.SqliteVec.ExtensionPath, StringComparison.Ordinal))
        {
            next.SqliteVec.ExtensionPath = current.SqliteVec.ExtensionPath;
            deferredPaths.Add("sqliteVec.extensionPath");
        }

        if (current.SqliteVec.VectorDimensions != loaded.SqliteVec.VectorDimensions)
        {
            next.SqliteVec.VectorDimensions = current.SqliteVec.VectorDimensions;
            deferredPaths.Add("sqliteVec.vectorDimensions");
        }

        return next;
    }

    public void Dispose()
    {
        _watcher.Changed -= OnConfigFileChanged;
        _watcher.Created -= OnConfigFileChanged;
        _watcher.Renamed -= OnConfigFileChanged;
        _watcher.Deleted -= OnConfigFileChanged;
        _watcher.Dispose();
        _reloadTimer.Dispose();
    }
}

public sealed class StaticRuntimeConfigCenter : IRuntimeConfigCenter
{
    private readonly AppConfig _snapshot;

    public StaticRuntimeConfigCenter(AppConfig config, string configPath = "in-memory")
    {
        _snapshot = config.Clone();
        ConfigPath = configPath;
    }

    public string ConfigPath { get; }

    public AppConfig GetSnapshot() => _snapshot.Clone();
}
