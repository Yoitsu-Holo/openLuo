using openLuo.Core.Interfaces;

namespace openLuo.Infrastructure.Logging;

/// <summary>
/// 静态日志 Facade。启动时 Initialize() 后全局可用，无需 DI 注入 IGameLogger。
/// </summary>
public static class Logger
{
    private static IGameLogger? _impl;
    private static readonly object _gate = new();

    /// <summary>启动时绑定真实日志实现。</summary>
    public static void Initialize(IGameLogger impl)
    {
        ArgumentNullException.ThrowIfNull(impl);
        lock (_gate) _impl = impl;
    }

    /// <summary>测试时释放引用。</summary>
    public static void Reset() { lock (_gate) _impl = null; }

    /// <summary>当前是否已初始化。</summary>
    public static bool IsInitialized { get { lock (_gate) return _impl is not null; } }

    private static IGameLogger Instance()
    {
        return _impl ?? throw new InvalidOperationException(
            "Logger is not initialized. Call Logger.Initialize(...) at startup to bind a log implementation.");
    }

    // ── 日志方法 ──

    public static void Info(string category, string message, object? data = null)
        => Instance().Info(category, message, data);

    public static void Warn(string category, string message, object? data = null)
        => Instance().Warn(category, message, data);

    public static void Error(string category, string message, object? data = null)
        => Instance().Error(category, message, data);

    public static void Debug(string category, string message, object? data = null)
        => Instance().Debug(category, message, data);

    public static void Plugin(string pluginId, string level, string msg, object? data = null)
        => Instance().Plugin(pluginId, level, msg, data);
}
