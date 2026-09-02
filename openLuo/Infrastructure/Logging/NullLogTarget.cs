using openLuo.Core.Interfaces;

namespace openLuo.Infrastructure.Logging;

/// <summary>
/// IGameLogger 的空实现，用于测试环境。所有日志静默丢弃。
/// 可选的 <see cref="CapturedLines"/> 列表供断言验证。
/// </summary>
public sealed class NullLogTarget : IGameLogger
{
    /// <summary>拦截的日志行。按写入顺序保存。</summary>
    public List<(string Level, string Category, string Message)> CapturedLines { get; } = [];

    public void Log(string level, string category, string message, string file = "", int line = 0)
        => CapturedLines.Add((level, category, message));

    public void Info(string category, string message, string file = "", int line = 0) => Capture("info", category, message);
    public void Info(string category, string message, object? data, string file = "", int line = 0) => Capture("info", category, message);

    public void Warn(string category, string message, string file = "", int line = 0) => Capture("warn", category, message);
    public void Warn(string category, string message, object? data, string file = "", int line = 0) => Capture("warn", category, message);

    public void Error(string category, string message, string file = "", int line = 0) => Capture("error", category, message);
    public void Error(string category, string message, object? data, string file = "", int line = 0) => Capture("error", category, message);

    public void Debug(string category, string message, string file = "", int line = 0) => Capture("debug", category, message);
    public void Debug(string category, string message, object? data, string file = "", int line = 0) => Capture("debug", category, message);

    public void Plugin(string pluginId, string level, string msg, object? data = null, string file = "", int line = 0)
        => Capture("plugin", $"{pluginId}/{level}", msg);

    private void Capture(string level, string category, string message)
        => CapturedLines.Add((level, category, message));
}
