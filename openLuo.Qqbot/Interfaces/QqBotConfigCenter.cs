using System.Text.Json;

namespace openLuo.Interfaces.QQbot;

public interface IQqBotConfigCenter
{
    QqBotConfig GetSnapshot();
}

public sealed class QqBotConfigCenter : IQqBotConfigCenter, IDisposable
{
    private readonly string _path;
    private readonly FileSystemWatcher _watcher;
    private volatile QqBotConfig _snapshot;

    public QqBotConfigCenter(string path)
    {
        _path = path;
        _snapshot = Load(path);
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path)) { EnableRaisingEvents = true };
        _watcher.Changed += Reload;
        _watcher.Created += Reload;
        _watcher.Renamed += Reload;
    }

    public QqBotConfig GetSnapshot() => _snapshot.Clone();
    private void Reload(object? sender, FileSystemEventArgs args)
    {
        try { _snapshot = Load(_path); } catch { }
    }
    private static QqBotConfig Load(string path) => JsonSerializer.Deserialize<QqBotConfig>(File.ReadAllText(path), new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true
    }) ?? new();
    public void Dispose() => _watcher.Dispose();
}
