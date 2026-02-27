using System.Text.Json;

namespace openLuo.Interfaces.QQbot;

public interface IQqBotConfigCenter
{
    string ConfigPath { get; }
    QqBotConfig GetSnapshot();
}

public sealed class QqBotConfigCenter : IQqBotConfigCenter, IDisposable
{
    private readonly string _configPath;
    private readonly FileSystemWatcher _watcher;
    private readonly object _reloadGate = new();
    private readonly Timer _reloadTimer;
    private volatile QqBotConfig _snapshot;

    public QqBotConfigCenter(string configPath)
    {
        _configPath = configPath;
        _snapshot = LoadConfig(configPath);
        _reloadTimer = new Timer(_ => ReloadFromDisk(), null, Timeout.Infinite, Timeout.Infinite);

        var directory = Path.GetDirectoryName(configPath);
        var fileName = Path.GetFileName(configPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException($"Invalid QQbot config path: {configPath}");

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnConfigFileChanged;
        _watcher.Created += OnConfigFileChanged;
        _watcher.Renamed += OnConfigFileChanged;
    }

    public string ConfigPath => _configPath;

    public QqBotConfig GetSnapshot() => _snapshot.Clone();

    private void OnConfigFileChanged(object? sender, FileSystemEventArgs e)
    {
        lock (_reloadGate)
            _reloadTimer.Change(250, Timeout.Infinite);
    }

    private void ReloadFromDisk()
    {
        try
        {
            _snapshot = LoadConfig(_configPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[qqbot-config] 配置热加载失败，继续使用当前快照：{ex.Message}");
        }
    }

    private static QqBotConfig LoadConfig(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<QqBotConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        }) ?? new QqBotConfig();
    }

    public void Dispose()
    {
        _watcher.Changed -= OnConfigFileChanged;
        _watcher.Created -= OnConfigFileChanged;
        _watcher.Renamed -= OnConfigFileChanged;
        _watcher.Dispose();
        _reloadTimer.Dispose();
    }
}
