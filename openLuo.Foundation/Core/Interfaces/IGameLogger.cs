namespace openLuo.Core.Interfaces;

/// <summary>
/// Provides structured logging for game events and diagnostics.
/// </summary>
public interface IGameLogger
{
    /// <summary>
    /// Log a message with specified level and category.
    /// </summary>
    /// <param name="level">Log level (Debug, Info, Warn, Error).</param>
    /// <param name="category">Log category for filtering (e.g., "Game", "Plugin", "Database").</param>
    /// <param name="message">Log message text.</param>
    void Log(string level, string category, string message);

    /// <summary>Log a debug message.</summary>
    /// <param name="category">Log category.</param>
    /// <param name="message">Debug message.</param>
    void Debug(string category, string message);

    /// <summary>Log a debug message with structured payload.</summary>
    void Debug(string category, string message, object? data);

    /// <summary>Log an info message.</summary>
    /// <param name="category">Log category.</param>
    /// <param name="message">Info message.</param>
    void Info(string category, string message);

    /// <summary>Log an info message with structured payload.</summary>
    void Info(string category, string message, object? data);

    /// <summary>Log a warning message.</summary>
    /// <param name="category">Log category.</param>
    /// <param name="message">Warning message.</param>
    void Warn(string category, string message);

    /// <summary>Log a warning message with structured payload.</summary>
    void Warn(string category, string message, object? data);

    /// <summary>Log an error message.</summary>
    /// <param name="category">Log category.</param>
    /// <param name="message">Error message.</param>
    void Error(string category, string message);

    /// <summary>Log an error message with structured payload.</summary>
    void Error(string category, string message, object? data);

    /// <summary>Write a plugin-originated log entry.</summary>
    void Plugin(string pluginId, string level, string msg, object? data = null);
}
