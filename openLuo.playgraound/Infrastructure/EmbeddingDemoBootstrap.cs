using Microsoft.Extensions.Configuration;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Embedding.Core.Interfaces;
using openLuo.Modules.Embedding.Infrastructure;

namespace openLuo.playgraound.Infrastructure;

internal static class EmbeddingDemoBootstrap
{
    public static IEmbeddingClient? TryCreateClient(out EmbeddingDemoSettings? settings, out string? error)
    {
        var configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "config", "embedding.demo.ini"));
        if (!File.Exists(configPath))
        {
            settings = null;
            error = $"Missing config file: {configPath}{Environment.NewLine}Create it from: {Path.ChangeExtension(configPath, ".example.ini")}";
            return null;
        }

        var configuration = new ConfigurationBuilder()
            .AddIniFile(configPath, optional: false, reloadOnChange: false)
            .Build();

        var embedding = new EmbeddingConfig
        {
            Enabled = ParseBool(configuration["embedding:enabled"], true),
            Provider = configuration["embedding:provider"] ?? "OpenAI",
            BaseUrl = configuration["embedding:baseUrl"] ?? "https://api.openai.com/v1/",
            ApiKey = configuration["embedding:apiKey"] ?? string.Empty,
            Model = configuration["embedding:model"] ?? "text-embedding-3-small",
            EndpointPath = configuration["embedding:endpointPath"] ?? "embeddings",
            TimeoutSeconds = ParseInt(configuration["embedding:timeoutSeconds"], 8),
            MaxRetryAttempts = ParseInt(configuration["embedding:maxRetryAttempts"], 2),
            BaseDelayMs = ParseInt(configuration["embedding:baseDelayMs"], 200)
        };

        settings = new EmbeddingDemoSettings
        {
            ConfigPath = configPath,
            Embedding = embedding,
            SqliteVecExtensionPath = configuration["sqliteVec:extensionPath"] ?? string.Empty,
            VectorDimensions = ParseInt(configuration["sqliteVec:vectorDimensions"], 1536),
            RequestDelayMs = ParseInt(configuration["demo:requestDelayMs"], 1200)
        };

        if (!embedding.Enabled)
        {
            error = $"Embedding is disabled in: {configPath}";
            return null;
        }

        if (string.IsNullOrWhiteSpace(embedding.ApiKey))
        {
            error = $"Invalid embedding.apiKey in: {configPath}";
            return null;
        }

        error = null;
        return EmbeddingClientFactory.Create(embedding, new ConsoleGameLogger());
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

    private static bool ParseBool(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;
}

internal sealed class EmbeddingDemoSettings
{
    public string ConfigPath { get; init; } = string.Empty;
    public EmbeddingConfig Embedding { get; init; } = new();
    public string SqliteVecExtensionPath { get; init; } = string.Empty;
    public int VectorDimensions { get; init; } = 1536;
    public int RequestDelayMs { get; init; } = 1200;
}
