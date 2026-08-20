using System.Runtime.CompilerServices;

namespace openLuo.Core.Interfaces;

/// <summary>
/// Provides structured logging for game events and diagnostics.
/// </summary>
/// <remarks>
/// 每个日志方法带 <see cref="CallerFilePathAttribute"/>/<see cref="CallerLineNumberAttribute"/>
/// 可选参数：编译器在调用点替换为调用方源文件与行号（零运行时开销），
/// 供终端元信息行与文件 JSON 的 source 字段使用。
/// </remarks>
public interface IGameLogger
{
    /// <summary>
    /// Log a message with specified level and category.
    /// </summary>
    /// <param name="level">Log level (Debug, Info, Warn, Error).</param>
    /// <param name="category">Log category for filtering (e.g., "Game", "Plugin", "Database").</param>
    /// <param name="message">Log message text.</param>
    void Log(string level, string category, string message,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

    /// <summary>Log a debug message.</summary>
    /// <param name="category">Log category.</param>
    /// <param name="message">Debug message.</param>
    void Debug(string category, string message,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

    /// <summary>Log a debug message with structured payload.</summary>
    void Debug(string category, string message, object? data,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

    /// <summary>Log an info message.</summary>
    /// <param name="category">Log category.</param>
    /// <param name="message">Info message.</param>
    void Info(string category, string message,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

    /// <summary>Log an info message with structured payload.</summary>
    void Info(string category, string message, object? data,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

    /// <summary>Log a warning message.</summary>
    /// <param name="category">Log category.</param>
    /// <param name="message">Warning message.</param>
    void Warn(string category, string message,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

    /// <summary>Log a warning message with structured payload.</summary>
    void Warn(string category, string message, object? data,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

    /// <summary>Log an error message.</summary>
    /// <param name="category">Log category.</param>
    /// <param name="message">Error message.</param>
    void Error(string category, string message,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

    /// <summary>Log an error message with structured payload.</summary>
    void Error(string category, string message, object? data,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);

    /// <summary>Write a plugin-originated log entry.</summary>
    void Plugin(string pluginId, string level, string msg, object? data = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);
}
