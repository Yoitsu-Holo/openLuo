using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Modules.Llm.Infrastructure.Chat;

public sealed class RuntimeConfiguredLlmClient : ILlmClient
{
    private readonly IRuntimeConfigCenter _configCenter;
    private readonly IGameLogger _logger;
    private readonly object _gate = new();
    private string? _cacheKey;
    private ILlmClient? _inner;

    public RuntimeConfiguredLlmClient(IRuntimeConfigCenter configCenter, IGameLogger logger)
    {
        _configCenter = configCenter;
        _logger = logger;
    }

    public Task<string> CompleteAsync(IEnumerable<LocalChatMessage> messages, LlmOptions? options = null, CancellationToken ct = default) =>
        GetInner().CompleteAsync(messages, options, ct);

    public Task<string> StreamAsync(IEnumerable<LocalChatMessage> messages, Action<string> onChunk, LlmOptions? options = null, CancellationToken ct = default) =>
        GetInner().StreamAsync(messages, onChunk, options, ct);

    private ILlmClient GetInner()
    {
        var config = _configCenter.GetSnapshot().Llm;
        var key = string.Join("|",
            config.Provider,
            config.BaseUrl,
            config.ApiKey,
            config.Model,
            config.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.MaxTokens?.ToString() ?? "null",
            config.TimeoutSeconds.ToString(),
            config.MaxRetryAttempts.ToString(),
            config.BaseDelayMs.ToString(),
            config.RateLimitPerMinute.ToString());

        lock (_gate)
        {
            if (_inner is not null && string.Equals(_cacheKey, key, StringComparison.Ordinal))
                return _inner;

            _cacheKey = key;
            _inner = LlmClientFactory.Create(config, _logger);
            return _inner;
        }
    }
}
