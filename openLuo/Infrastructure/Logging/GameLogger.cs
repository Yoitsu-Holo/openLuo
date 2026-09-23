using System.Runtime.CompilerServices;
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

    public void Log(string level, string category, string message,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var lv = Enum.TryParse<LogLevel>(level, true, out var l) ? l : LogLevel.Info;
        Write(lv, category, message, null, file, line);
    }

    public void Info(string category, string message,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Info, category, message, null, file, line);
    public void Warn(string category, string message,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Warn, category, message, null, file, line);
    public void Error(string category, string message,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Error, category, message, null, file, line);
    public void Debug(string category, string message,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Debug, category, message, null, file, line);

    public void Info(string category, string msg, object? data,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Info, category, msg, data, file, line);
    public void Warn(string category, string msg, object? data,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Warn, category, msg, data, file, line);
    public void Error(string category, string msg, object? data,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Error, category, msg, data, file, line);
    public void Debug(string category, string msg, object? data,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Write(LogLevel.Debug, category, msg, data, file, line);

    /// <summary>Called by game/log MCP interface from plugins.</summary>
    public void Plugin(string pluginId, string level, string msg, object? data = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var lv = Enum.TryParse<LogLevel>(level, true, out var l) ? l : LogLevel.Info;
        if (lv > GetEffectiveLevel("plugin")) return;
        var entry = MakeEntry(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), lv, pluginId, file, line, msg, data);
        var path = Path.Combine(_pluginDir, $"{Sanitize(pluginId)}.jsonl");
        AppendLine(path, entry);
    }

    // ── internals ───────────────────────────────────────────────

    private void Write(LogLevel lv, string category, string msg, object? data, string file, int line)
    {
        var effectiveLevel = GetEffectiveLevel(category);
        if (lv > effectiveLevel) return;
        // 时间戳只取一次：文件 JSON 的 ts 与终端元信息行共用，保证两侧逐字符一致。
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var module = ModuleOf(category);
        var entry = MakeEntry(ts, lv, module, file, line, msg, data);
        var path = Path.Combine(_coreDir, $"{Sanitize(category)}.jsonl");
        AppendLine(path, entry);
        if (ShouldOutputToConsole() && _streams is not null)
        {
            // 1+N 行：首行元信息 [ts] [level] [source] [module]，后续为内容行。
            // 单次 Write 拼成一个字符串（内部 \n 分隔），避免多线程下日志交错。
            var color = lv switch
            {
                LogLevel.Debug => "\x1b[90m",
                LogLevel.Info => "\x1b[32m",
                LogLevel.Warn => "\x1b[33m",
                LogLevel.Error => "\x1b[31m",
                _ => ""
            };
            var levelText = lv.ToString().ToLower();
            var content = data is null ? msg : $"{msg} {JsonSerializer.Serialize(data, _jsonOptions)}";
            var consoleLine = $"[{ts}] [{color}{levelText}\x1b[0m] [{Path.GetFileName(file)}:{line}] [{module}]\n{content}\n";
            var bytes = Encoding.UTF8.GetBytes(consoleLine);
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

    /// <summary>module = category 第一个 '/' 前的段（agent/dispatch → agent）；无 '/' 时即 category 本身。</summary>
    private static string ModuleOf(string category)
    {
        var idx = category.IndexOf('/');
        return idx < 0 ? category : category[..idx];
    }

    private static string MakeEntry(string ts, LogLevel lv, string module, string file, int line, string msg, object? data)
    {
        var source = $"{Path.GetFileName(file)}:{line}";
        var obj = data is null
            ? (object)new { ts, level = lv.ToString().ToLower(), module, source, msg }
            : new { ts, level = lv.ToString().ToLower(), module, source, msg, data };
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
