using System.ClientModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat.Base;
using OpenAI;
using OpenAI.Chat;
using MsAiChatClient = Microsoft.Extensions.AI.IChatClient;
using MsAiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using MsAiChatOptions = Microsoft.Extensions.AI.ChatOptions;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;
using AIContent = Microsoft.Extensions.AI.AIContent;

namespace openLuo.Modules.Llm.Infrastructure.Chat.Adapters;

public class OpenAiCompatibleLlmClient : MicrosoftAiLlmClient
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MsAiChatClient _client;

    public OpenAiCompatibleLlmClient(LlmConfig config, IGameLogger logger) : base(config, logger)
    {
        _client = CreateClient(config.Provider, config.BaseUrl, config.ApiKey, config.Model);
    }

    protected override Task<string> CompleteCoreAsync(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, CancellationToken ct) =>
        ShouldUseCustomRequest(options)
            ? CompleteBlockingCustomAsync(messages, options, ct)
            : CompleteBlockingStandardAsync(messages, options, ct);

    protected override Task<string> StreamCoreAsync(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, Action<string> onChunk, CancellationToken ct) =>
        ShouldUseCustomRequest(options)
            ? CompleteStreamingCustomAsync(messages, options, onChunk, ct)
            : CompleteStreamingStandardAsync(messages, options, onChunk, ct);

    protected virtual bool ShouldUseCustomRequest(LlmOptions options) => options.ExtraBody.Count > 0;

    protected virtual string BuildCustomChatUrl()
    {
        var baseUrl = (Config.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("LLM baseUrl is required.");

        if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return baseUrl;
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return baseUrl + "/chat/completions";
        return baseUrl + "/chat/completions";
    }

    protected virtual Dictionary<string, object?> BuildCustomBody(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, bool stream)
    {
        var body = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = Config.Model,
            ["messages"] = messages.Select(m => new Dictionary<string, object?>
            {
                ["role"] = ToProtocolRole(m.Role),
                ["content"] = SerializeContent(m)
            }).ToArray(),
            ["stream"] = stream
        };

        if (options.Temperature is not null)
            body["temperature"] = options.Temperature;
        if (options.MaxTokens is not null)
            body["max_tokens"] = options.MaxTokens;
        foreach (var pair in options.ExtraBody)
            body[pair.Key] = pair.Value;
        return body;
    }

    protected virtual string ExtractTextFromCustomResponse(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject();
        var content = node?["choices"]?[0]?["message"]?["content"];
        if (content is JsonValue value)
            return value.ToString();

        if (content is JsonArray parts)
            return string.Concat(parts.Select(part => part?["text"]?.ToString() ?? part?["content"]?.ToString() ?? string.Empty));

        return string.Empty;
    }

    protected virtual string ExtractStreamChunk(string line)
    {
        var payload = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? line[5..].Trim()
            : line.Trim();
        if (string.IsNullOrWhiteSpace(payload) || payload == "[DONE]")
            return string.Empty;

        var node = JsonNode.Parse(payload)?.AsObject();
        var delta = node?["choices"]?[0]?["delta"]?["content"];
        if (delta is JsonValue value)
            return value.ToString();

        if (delta is JsonArray parts)
            return string.Concat(parts.Select(part => part?["text"]?.ToString() ?? part?["content"]?.ToString() ?? string.Empty));

        var message = node?["choices"]?[0]?["message"]?["content"];
        return message?.ToString() ?? string.Empty;
    }

    protected override LlmOptions NormalizeOptions(LlmOptions options) => new()
    {
        Temperature = options.Temperature,
        MaxTokens = options.MaxTokens,
        ExtraBody = new Dictionary<string, object?>(options.ExtraBody, StringComparer.OrdinalIgnoreCase)
    };

    private Task<string> CompleteBlockingStandardAsync(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, CancellationToken ct)
    {
        var chatOptions = new MsAiChatOptions
        {
            Temperature = options.Temperature,
            MaxOutputTokens = options.MaxTokens
        };

        Logger.Info("llm", $"LLM request started: provider={Config.Provider}, model={Config.Model}, temp={chatOptions.Temperature}, messages={messages.Count}");
        Logger.Debug("llm", $"LLM prompt: {JsonSerializer.Serialize(messages.Select(m => new { m.Role, m.Content }), LogOptions)}");

        return ExecuteWithRetryAsync("LLM request", async linked =>
        {
            var response = await _client.GetResponseAsync(ToAiMessages(messages), chatOptions, linked);
            var text = response.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("LLM returned empty content.");
            Logger.Info("llm", $"LLM response (raw): {text}");
            return text;
        }, ct);
    }

    private Task<string> CompleteStreamingStandardAsync(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, Action<string> onChunk, CancellationToken ct)
    {
        var chatOptions = new MsAiChatOptions
        {
            Temperature = options.Temperature,
            MaxOutputTokens = options.MaxTokens
        };

        Logger.Info("llm", $"LLM streaming request started: provider={Config.Provider}, model={Config.Model}, temp={chatOptions.Temperature}, messages={messages.Count}");
        Logger.Debug("llm", $"LLM prompt: {JsonSerializer.Serialize(messages.Select(m => new { m.Role, m.Content }), LogOptions)}");

        return ExecuteWithRetryAsync("LLM streaming request", async linked =>
        {
            var chunks = new List<string>();
            await foreach (var update in _client.GetStreamingResponseAsync(ToAiMessages(messages), chatOptions, linked))
            {
                var text = update.Text;
                if (string.IsNullOrEmpty(text))
                    continue;

                chunks.Add(text);
                onChunk(text);
            }

            var fullResponse = string.Concat(chunks);
            if (string.IsNullOrWhiteSpace(fullResponse))
                throw new InvalidOperationException("LLM streaming returned empty content.");
            Logger.Info("llm", $"LLM response (raw): {fullResponse}");
            return fullResponse;
        }, ct);
    }

    private Task<string> CompleteBlockingCustomAsync(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, CancellationToken ct)
    {
        Logger.Info("llm", $"LLM custom request started: provider={Config.Provider}, model={Config.Model}, temp={options.Temperature}, messages={messages.Count}");
        Logger.Debug("llm", $"LLM custom prompt: {JsonSerializer.Serialize(messages.Select(m => new { m.Role, m.Content }), LogOptions)}");

        return ExecuteWithRetryAsync("LLM custom request", async linked =>
        {
            using var http = CreateHttpClient();
            using var request = BuildCustomRequest(messages, options, stream: false);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked);
            var body = await response.Content.ReadAsStringAsync(linked);
            response.EnsureSuccessStatusCode();
            var text = ExtractTextFromCustomResponse(body);
            if (string.IsNullOrWhiteSpace(text))
            {
                Logger.Warn("llm", $"LLM returned empty content. Raw response body: {body}");
                throw new InvalidOperationException("LLM returned empty content.");
            }
            Logger.Info("llm", $"LLM custom response (raw): {text}");
            return text;
        }, ct);
    }

    private Task<string> CompleteStreamingCustomAsync(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, Action<string> onChunk, CancellationToken ct)
    {
        Logger.Info("llm", $"LLM custom streaming request started: provider={Config.Provider}, model={Config.Model}, temp={options.Temperature}, messages={messages.Count}");
        Logger.Debug("llm", $"LLM custom prompt: {JsonSerializer.Serialize(messages.Select(m => new { m.Role, m.Content }), LogOptions)}");

        return ExecuteWithRetryAsync("LLM custom streaming request", async linked =>
        {
            using var http = CreateHttpClient();
            using var request = BuildCustomRequest(messages, options, stream: true);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(linked);
            using var reader = new StreamReader(stream);

            var chunks = new List<string>();
            while (true)
            {
                var line = await reader.ReadLineAsync(linked);
                if (line is null)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var text = ExtractStreamChunk(line);
                if (string.IsNullOrEmpty(text))
                    continue;
                chunks.Add(text);
                onChunk(text);
            }

            var fullResponse = string.Concat(chunks);
            if (string.IsNullOrWhiteSpace(fullResponse))
                throw new InvalidOperationException("LLM streaming returned empty content.");
            Logger.Info("llm", $"LLM custom streaming response (raw): {fullResponse}");
            return fullResponse;
        }, ct);
    }

    private HttpClient CreateHttpClient()
    {
        var http = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        if (!string.IsNullOrWhiteSpace(Config.ApiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Config.ApiKey);
        return http;
    }

    private HttpRequestMessage BuildCustomRequest(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, bool stream) =>
        new(HttpMethod.Post, BuildCustomChatUrl())
        {
            Content = new StringContent(JsonSerializer.Serialize(BuildCustomBody(messages, options, stream), HttpJsonOptions), Encoding.UTF8, "application/json")
        };

    private static MsAiChatClient CreateClient(LlmProvider provider, string baseUrl, string apiKey, string model)
    {
        var normalizedBaseUrl = NormalizeChatBaseUrl(baseUrl);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(normalizedBaseUrl)
        };
        return new ChatClient(model, new ApiKeyCredential(ResolveApiKey(apiKey, provider, baseUrl)), options).AsIChatClient();
    }

    private static string NormalizeChatBaseUrl(string baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("LLM baseUrl is required.");
        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed + "/"
            : trimmed + "/";
    }

    private static string ResolveApiKey(string apiKey, LlmProvider provider, string baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey;
        return IsOllamaEndpoint(provider, baseUrl) ? "ollama" : string.Empty;
    }

    private static bool IsOllamaEndpoint(LlmProvider provider, string baseUrl)
    {
        if (provider == LlmProvider.Ollama)
            return true;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        return uri.Port == 11434;
    }

    private static IList<MsAiChatMessage> ToAiMessages(IEnumerable<LocalChatMessage> messages) =>
        messages.Select(m =>
        {
            if (m.Blocks is { Count: > 0 })
            {
                var contents = m.Blocks.Select(BlockToAiContent).Where(c => c is not null).Select(c => c!).ToList();
                if (contents.Count > 0)
                    return new MsAiChatMessage(MapRole(m.Role), contents) { RawRepresentation = m };
            }
            return new MsAiChatMessage(MapRole(m.Role), m.Content) { RawRepresentation = m };
        }).ToList();

    private static ChatRole MapRole(openLuo.Modules.Llm.Core.Models.ChatMessageRole role) => role switch
    {
        openLuo.Modules.Llm.Core.Models.ChatMessageRole.System => ChatRole.System,
        openLuo.Modules.Llm.Core.Models.ChatMessageRole.Assistant => ChatRole.Assistant,
        openLuo.Modules.Llm.Core.Models.ChatMessageRole.Tool => ChatRole.Tool,
        _ => ChatRole.User
    };

    private static string ToProtocolRole(openLuo.Modules.Llm.Core.Models.ChatMessageRole role) => role switch
    {
        openLuo.Modules.Llm.Core.Models.ChatMessageRole.System => "system",
        openLuo.Modules.Llm.Core.Models.ChatMessageRole.Assistant => "assistant",
        openLuo.Modules.Llm.Core.Models.ChatMessageRole.Tool => "tool",
        _ => "user"
    };

    private static object SerializeContent(LocalChatMessage message)
    {
        if (message.Blocks is not { Count: > 0 })
            return message.Content;

        var parts = message.Blocks
            .Select(BlockToContentPart)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        return parts.Count > 0 ? (object)parts : message.Content;
    }

    private static Dictionary<string, object?>? BlockToContentPart(Block block) => block switch
    {
        TextBlock text => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = "text",
            ["text"] = text.Text
        },
        ImageBlock image => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = "image_url",
            ["image_url"] = new Dictionary<string, object?>
            {
                ["url"] = ResolveImageUrl(image)
            }
        },
        _ => null
    };

    private static AIContent? BlockToAiContent(Block block) => block switch
    {
        TextBlock text => new TextContent(text.Text),
        ImageBlock image => new UriContent(ResolveImageUrl(image), image.MimeType),
        _ => null
    };

    private static string ResolveImageUrl(ImageBlock image)
    {
        // AssetId might be a URL (http/https), a data URI, or an internal asset reference.
        // Internal asset references need to be resolved through the asset store at a higher level.
        if (image.AssetId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            image.AssetId.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            image.AssetId.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return image.AssetId;

        // Fallback: construct a data URI placeholder. Real resolution requires IAssetStore.
        return $"asset://{image.AssetId}";
    }
}
