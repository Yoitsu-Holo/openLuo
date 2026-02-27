using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using openLuo.Core.Interfaces;
using openLuo.Infrastructure.Security;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Modules.Llm.Infrastructure.Chat.Base;

public abstract class MicrosoftAiLlmClient : ILlmClient
{
    protected static readonly JsonSerializerOptions LogOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    protected readonly IGameLogger Logger;
    protected readonly LlmConfig Config;

    private readonly LlmOptions _defaultOptions;
    private readonly Dictionary<string, (int Count, DateTime ResetTime)> _rateLimits = new();

    protected MicrosoftAiLlmClient(LlmConfig config, IGameLogger logger)
    {
        Config = config.Clone();
        Logger = logger;
        _defaultOptions = new LlmOptions
        {
            Temperature = config.Temperature,
            MaxTokens = config.MaxTokens
        };
    }

    public Task<string> CompleteAsync(IEnumerable<LocalChatMessage> messages, LlmOptions? options = null, CancellationToken ct = default)
    {
        CheckRateLimit("llm_complete");
        return CompleteCoreAsync(SanitizeMessages(messages), NormalizeOptions(MergeOptions(options)), ct);
    }

    public Task<string> StreamAsync(IEnumerable<LocalChatMessage> messages, Action<string> onChunk, LlmOptions? options = null, CancellationToken ct = default)
    {
        CheckRateLimit("llm_stream");
        return StreamCoreAsync(SanitizeMessages(messages), NormalizeOptions(MergeOptions(options)), onChunk, ct);
    }

    protected abstract Task<string> CompleteCoreAsync(
        IReadOnlyList<LocalChatMessage> messages,
        LlmOptions options,
        CancellationToken ct);

    protected abstract Task<string> StreamCoreAsync(
        IReadOnlyList<LocalChatMessage> messages,
        LlmOptions options,
        Action<string> onChunk,
        CancellationToken ct);

    protected virtual LlmOptions NormalizeOptions(LlmOptions options) => options;

    protected LlmOptions MergeOptions(LlmOptions? options) => new()
    {
        Temperature = options?.Temperature ?? _defaultOptions.Temperature,
        MaxTokens = options?.MaxTokens ?? _defaultOptions.MaxTokens,
        JsonMode = options?.JsonMode ?? false,
        ExtraBody = options?.ExtraBody is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(options.ExtraBody, StringComparer.OrdinalIgnoreCase)
    };

    protected IReadOnlyList<LocalChatMessage> SanitizeMessages(IEnumerable<LocalChatMessage> messages) =>
        messages.Select(m =>
        {
            var sanitized = m.Role == ChatMessageRole.User
                ? PromptSanitizer.SanitizeForPrompt(m.Content)
                : m.Content;
            return new LocalChatMessage(m.Role, sanitized) { Blocks = m.Blocks };
        }).ToList();

    protected CancellationTokenSource CreateTimeoutCts(CancellationToken ct, int minimumSeconds = 1)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(Math.Max(minimumSeconds, Config.TimeoutSeconds)));
        return linked;
    }

    protected static int GetBackoffDelayMs(int attempt, int baseDelay)
    {
        var jitter = Random.Shared.Next(0, 100);
        return Math.Min(10_000, (int)(baseDelay * Math.Pow(2, attempt)) + jitter);
    }

    protected void CheckRateLimit(string key)
    {
        var now = DateTime.UtcNow;
        if (_rateLimits.TryGetValue(key, out var limit))
        {
            if (now < limit.ResetTime)
            {
                if (Config.RateLimitPerMinute > 0 && limit.Count >= Config.RateLimitPerMinute)
                    throw new InvalidOperationException("Rate limit exceeded");

                _rateLimits[key] = (limit.Count + 1, limit.ResetTime);
            }
            else
            {
                _rateLimits[key] = (1, now.AddMinutes(1));
            }
        }
        else
        {
            _rateLimits[key] = (1, now.AddMinutes(1));
        }
    }

    protected async Task<string> ExecuteWithRetryAsync(
        string operationName,
        Func<CancellationToken, Task<string>> action,
        CancellationToken ct)
    {
        var retries = Math.Max(0, Config.MaxRetryAttempts);
        var delayBase = Math.Max(50, Config.BaseDelayMs);
        var sw = Stopwatch.StartNew();

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                using var linked = CreateTimeoutCts(ct);
                var result = await action(linked.Token);
                sw.Stop();
                Logger.Log(sw.ElapsedMilliseconds > 10000 ? "warn" : "info", "llm", $"{operationName} completed: duration={sw.ElapsedMilliseconds}ms");
                return result;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < retries)
            {
                var delay = GetBackoffDelayMs(attempt, delayBase);
                Logger.Warn("llm", $"{operationName} timeout, retrying in {delay}ms (attempt {attempt + 1}/{retries})");
                await Task.Delay(delay, ct);
            }
            catch (HttpRequestException ex) when (attempt < retries)
            {
                var delay = GetBackoffDelayMs(attempt, delayBase);
                Logger.Warn("llm", $"{operationName} failed, retrying in {delay}ms (attempt {attempt + 1}/{retries}): {ex.Message}");
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Error("llm", $"{operationName} failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                throw;
            }
        }

        throw new HttpRequestException($"{operationName} failed after {retries} retries");
    }
}
