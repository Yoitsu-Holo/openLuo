using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat.Base;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Modules.Llm.Infrastructure.Chat.Providers;

public sealed class OllamaLlmClient : MicrosoftAiLlmClient
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    public OllamaLlmClient(LlmConfig config, IGameLogger logger) : base(config, logger)
    {
    }

    protected override LlmOptions NormalizeOptions(LlmOptions options)
    {
        var normalized = base.NormalizeOptions(options);
        if (IsQwenModel(Config) && !normalized.ExtraBody.ContainsKey("think"))
            normalized.ExtraBody["think"] = false;
        return normalized;
    }

    protected override Task<string> CompleteCoreAsync(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, CancellationToken ct)
    {
        Logger.Info("llm", $"LLM custom request started: provider={Config.Provider}, model={Config.Model}, temp={options.Temperature}, messages={messages.Count}");
        Logger.Debug("llm", $"LLM custom prompt: {JsonSerializer.Serialize(messages.Select(m => new { m.Role, m.Content }), LogOptions)}");

        return ExecuteWithRetryAsync("LLM custom request", async linked =>
        {
            using var http = CreateHttpClient();
            using var request = BuildRequest(messages, options, stream: false);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked);
            var body = await response.Content.ReadAsStringAsync(linked);
            response.EnsureSuccessStatusCode();
            var text = ExtractTextFromResponse(body);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("LLM returned empty content.");
            Logger.Debug("llm", $"LLM custom response (raw): {text}");
            return text;
        }, ct);
    }

    protected override Task<string> StreamCoreAsync(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, Action<string> onChunk, CancellationToken ct)
    {
        Logger.Info("llm", $"LLM custom streaming request started: provider={Config.Provider}, model={Config.Model}, temp={options.Temperature}, messages={messages.Count}");
        Logger.Debug("llm", $"LLM custom prompt: {JsonSerializer.Serialize(messages.Select(m => new { m.Role, m.Content }), LogOptions)}");

        return ExecuteWithRetryAsync("LLM custom streaming request", async linked =>
        {
            using var http = CreateHttpClient();
            using var request = BuildRequest(messages, options, stream: true);
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
            Logger.Debug("llm", $"LLM custom streaming response (raw): {fullResponse}");
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

    private HttpRequestMessage BuildRequest(IReadOnlyList<LocalChatMessage> messages, LlmOptions options, bool stream)
    {
        var body = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = Config.Model,
            ["messages"] = messages.Select(m => new Dictionary<string, object?>
            {
                ["role"] = ToProtocolRole(m.Role),
                ["content"] = m.Blocks is { Count: > 0 }
                    ? (object)m.Blocks.Select(OllamaBlockToPart).Where(p => p is not null).Select(p => p!).ToList()
                    : m.Content
            }).ToArray(),
            ["stream"] = stream
        };

        var nativeOptions = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (options.Temperature is not null)
            nativeOptions["temperature"] = options.Temperature;
        if (nativeOptions.Count > 0)
            body["options"] = nativeOptions;
        if (options.MaxTokens is not null)
            body["max_output_tokens"] = options.MaxTokens;

        foreach (var pair in options.ExtraBody)
            body[pair.Key] = pair.Value;

        return new HttpRequestMessage(HttpMethod.Post, BuildApiChatUrl())
        {
            Content = new StringContent(JsonSerializer.Serialize(body, HttpJsonOptions), Encoding.UTF8, "application/json")
        };
    }

    private string BuildApiChatUrl()
    {
        var baseUrl = (Config.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("LLM baseUrl is required.");
        if (baseUrl.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            return baseUrl;
        if (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            return baseUrl + "/chat";
        return baseUrl + "/api/chat";
    }

    private static string ExtractTextFromResponse(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject();
        var content = node?["message"]?["content"];
        return content?.ToString() ?? string.Empty;
    }

    private static string ExtractStreamChunk(string line)
    {
        var node = JsonNode.Parse(line)?.AsObject();
        var content = node?["message"]?["content"];
        return content?.ToString() ?? string.Empty;
    }

    private static string ToProtocolRole(ChatMessageRole role) => role switch
    {
        ChatMessageRole.System => "system",
        ChatMessageRole.Assistant => "assistant",
        ChatMessageRole.Tool => "tool",
        _ => "user"
    };

    private static bool IsQwenModel(LlmConfig config) =>
        config.Provider == LlmProvider.Qwen
        || (config.Model ?? string.Empty).Contains("Qwen", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, object?>? OllamaBlockToPart(Block block) => block switch
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
                ["url"] = !string.IsNullOrWhiteSpace(image.DataUri)
                    ? image.DataUri
                    : image.AssetId.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                      || image.AssetId.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                        ? image.AssetId
                        : $"asset://{image.AssetId}"
            }
        },
        _ => null
    };
}
