using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using openLuo.Modules.AppShell.Application;
using openLuo.Core.Interfaces;
using openLuo.Infrastructure.Config;

namespace openLuo.Infrastructure.Logging;

public enum LogLevel { Off, Error, Warn, Info, Debug }

public class GameLogger : IGameLogger
{
    private readonly LogConfig? _staticConfig;
    private readonly string _coreDir;
    private readonly string _pluginDir;
    private readonly IGameStreams? _streams;
    private static readonly object _lock = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public GameLogger(string logBaseDir, string levelStr, IGameStreams? streams = null)
    {
        _staticConfig = new LogConfig { Level = levelStr, OutputToConsole = false };
        _streams = streams;
        _coreDir = Path.Combine(logBaseDir, "core");
        _pluginDir = Path.Combine(logBaseDir, "plugin");
        Directory.CreateDirectory(_coreDir);
        Directory.CreateDirectory(_pluginDir);
    }

    public GameLogger(string logBaseDir, Modules.AppShell.Application.LogConfig? config = null, IGameStreams? streams = null)
    {
        _staticConfig = config?.Clone();
        _streams = streams;
        _coreDir = Path.Combine(logBaseDir, "core");
        _pluginDir = Path.Combine(logBaseDir, "plugin");
        Directory.CreateDirectory(_coreDir);
        Directory.CreateDirectory(_pluginDir);
    }
    // (constructor accepting IRuntimeConfigCenter removed — now uses static openLuo.Infrastructure.Config.Config.Log)

    private bool ShouldOutputToConsole() => GetLogConfig().OutputToConsole;

    // ── public API ──────────────────────────────────────────────

    public void Log(string level, string category, string message)
    {
        var lv = Enum.TryParse<LogLevel>(level, true, out var l) ? l : LogLevel.Info;
        Write(lv, category, message, null);
    }

    public void Info(string category, string message) => Write(LogLevel.Info, category, message, null);
    public void Warn(string category, string message) => Write(LogLevel.Warn, category, message, null);
    public void Error(string category, string message) => Write(LogLevel.Error, category, message, null);
    public void Debug(string category, string message) => Write(LogLevel.Debug, category, message, null);

    public void Info(string category, string msg, object? data) => Write(LogLevel.Info, category, msg, data);
    public void Warn(string category, string msg, object? data) => Write(LogLevel.Warn, category, msg, data);
    public void Error(string category, string msg, object? data) => Write(LogLevel.Error, category, msg, data);
    public void Debug(string category, string msg, object? data) => Write(LogLevel.Debug, category, msg, data);

    /// <summary>Called by game/log MCP interface from plugins.</summary>
    public void Plugin(string pluginId, string level, string msg, object? data = null)
    {
        var lv = Enum.TryParse<LogLevel>(level, true, out var l) ? l : LogLevel.Info;
        if (lv > GetEffectiveLevel("plugin")) return;
        var entry = MakeEntry(lv, msg, data);
        var file = Path.Combine(_pluginDir, $"{Sanitize(pluginId)}.jsonl");
        AppendLine(file, entry);
    }

    // ── internals ───────────────────────────────────────────────

    private void Write(LogLevel lv, string category, string msg, object? data)
    {
        var effectiveLevel = GetEffectiveLevel(category);
        if (lv > effectiveLevel) return;
        var entry = MakeEntry(lv, msg, data);
        var file = Path.Combine(_coreDir, $"{Sanitize(category)}.jsonl");
        AppendLine(file, entry);
        if (ShouldOutputToConsole() && _streams is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(entry + "\n");
            _streams.Error.Write(bytes);
            _streams.Error.Flush();
        }
    }

    private LogLevel GetEffectiveLevel(string category) =>
        GetLogConfig().Categories.TryGetValue(category, out var catLevel) && Enum.TryParse<LogLevel>(catLevel, true, out var l)
            ? l
            : ParseLevel(GetLogConfig().Level);

    // 实例配置优先（测试/显式构造）；否则读静态 Config.Log（运行时热重载）。
    private LogConfig GetLogConfig() => _staticConfig?.Clone() ?? openLuo.Infrastructure.Config.Config.Log ?? new LogConfig();

    private static LogLevel ParseLevel(string? level) =>
        Enum.TryParse<LogLevel>(level, true, out var parsed) ? parsed : LogLevel.Info;

    private static string MakeEntry(LogLevel lv, string msg, object? data)
    {
        var obj = data is null
            ? (object)new { ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), level = lv.ToString().ToLower(), msg }
            : new { ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), level = lv.ToString().ToLower(), msg, data };
        return JsonSerializer.Serialize(obj, _jsonOptions);
    }

    private static void AppendLine(string path, string line)
    {
        lock (_lock)
        {
            try { File.AppendAllText(path, line + "\n"); } catch { }
        }
    }

    private static string Sanitize(string s) => s.Replace("/", "-").Replace("\\", "-");
}
