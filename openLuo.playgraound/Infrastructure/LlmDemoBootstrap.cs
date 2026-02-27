using Microsoft.Extensions.Configuration;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Infrastructure.Chat;

namespace openLuo.playgraound.Infrastructure;

internal static class LlmDemoBootstrap
{
    public static ILlmClient? TryCreateClient(out string? error)
    {
        var configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "config", "llm.demo.ini"));
        if (!File.Exists(configPath))
        {
            error = $"Missing config file: {configPath}{Environment.NewLine}Create it from: {Path.ChangeExtension(configPath, ".example.ini")}";
            return null;
        }

        var configuration = new ConfigurationBuilder()
            .AddIniFile(configPath, optional: false, reloadOnChange: false)
            .Build();

        var config = new LlmConfig
        {
            Provider = ParseProvider(configuration["llm:provider"]),
            BaseUrl = configuration["llm:baseUrl"] ?? "http://localhost:11434",
            ApiKey = configuration["llm:apiKey"] ?? string.Empty,
            Model = configuration["llm:model"] ?? "qwen3:8b",
            Temperature = ParseFloat(configuration["llm:temperature"], 0.2f),
            MaxTokens = ParseNullableInt(configuration["llm:maxTokens"], 256),
            TimeoutSeconds = ParseInt(configuration["llm:timeoutSeconds"], 60),
            MaxRetryAttempts = ParseInt(configuration["llm:maxRetryAttempts"], 2),
            BaseDelayMs = ParseInt(configuration["llm:baseDelayMs"], 200),
            RateLimitPerMinute = ParseInt(configuration["llm:rateLimitPerMinute"], 0),
            Streaming = ParseBool(configuration["llm:streaming"], false)
        };

        if (config.Provider != LlmProvider.Ollama &&
            (string.IsNullOrWhiteSpace(config.ApiKey) ||
             string.Equals(config.ApiKey, "sk-your-api-key-here", StringComparison.Ordinal)))
        {
            error = $"Invalid apiKey in: {configPath}";
            return null;
        }

        error = null;
        return LlmClientFactory.Create(config, new ConsoleGameLogger());
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

    private static int? ParseNullableInt(string? value, int? fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            return null;
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static float ParseFloat(string? value, float fallback) =>
        float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool ParseBool(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static LlmProvider ParseProvider(string? value)
    {
        if (Enum.TryParse<LlmProvider>(value, ignoreCase: true, out var provider))
            return provider;
        return LlmProvider.Ollama;
    }
}
