using System.Text.Json;
using openLuo.Core.Interfaces;

namespace openLuo.playgraound.Infrastructure;

internal sealed class ConsoleGameLogger : IGameLogger
{
    public void Log(string level, string category, string message) =>
        Write(level, category, message, null);

    public void Debug(string category, string message) =>
        Write("debug", category, message, null);

    public void Debug(string category, string message, object? data) =>
        Write("debug", category, message, data);

    public void Info(string category, string message) =>
        Write("info", category, message, null);

    public void Info(string category, string message, object? data) =>
        Write("info", category, message, data);

    public void Warn(string category, string message) =>
        Write("warn", category, message, null);

    public void Warn(string category, string message, object? data) =>
        Write("warn", category, message, data);

    public void Error(string category, string message) =>
        Write("error", category, message, null);

    public void Error(string category, string message, object? data) =>
        Write("error", category, message, data);

    public void Plugin(string pluginId, string level, string msg, object? data = null) =>
        Write(level, $"plugin:{pluginId}", msg, data);

    private static void Write(string level, string category, string message, object? data)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] [{category}] {message}";
        if (data is null)
        {
            Console.WriteLine(line);
            return;
        }

        Console.WriteLine($"{line} {JsonSerializer.Serialize(data)}");
    }
}
