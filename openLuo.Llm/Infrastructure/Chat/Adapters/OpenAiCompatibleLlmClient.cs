using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Core.Models;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat.Base;
using static openLuo.Infrastructure.Logging.Logger;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Modules.Llm.Infrastructure.Chat.Adapters;

public class OpenAiCompatibleLlmClient : LlmClientBase
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    public OpenAiCompatibleLlmClient(LlmRouteConfig config) : base(config)
    {
    }

    protected override Task<LlmChatResponse> CompleteCoreAsync(
        IReadOnlyList<LocalChatMessage> messages, LlmOptions options, CancellationToken ct) =>
        CompleteAsync(messages, options, ct);

    protected override Task<string> StreamCoreAsync(
        IReadOnlyList<LocalChatMessage> messages, LlmOptions options, Action<string> onChunk, CancellationToken ct) =>
        StreamAsync(messages, options, onChunk, ct);

    // ── Request building ────────────────────────────────────────────────

    protected virtual string BuildChatUrl()
    {
        var baseUrl = (Config.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("LLM baseUrl is required.");
        return baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : baseUrl + "/chat/completions";
    }

    protected virtual Dictionary<string, object?> BuildBody(
        IReadOnlyList<LocalChatMessage> messages, LlmOptions options, bool stream)
    {
        var enableMultimodal = options.EnableMultimodal && (Config.Capabilities?.SupportsVision ?? false);
        var body = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = Config.Model,
            ["messages"] = messages.Select(SerializeMessage).ToArray(),
            ["stream"] = stream
        };

        if (options.Temperature is not null) body["temperature"] = options.Temperature;
        if (options.MaxTokens is not null) body["max_tokens"] = options.MaxTokens;
        if (options.Tools is { Count: > 0 }) body["tools"] = options.Tools.Select(ToToolSchema).ToArray();
        foreach (var pair in options.ExtraBody) body[pair.Key] = pair.Value;
        return body;

        Dictionary<string, object?> SerializeMessage(LocalChatMessage m)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["role"] = ToProtocolRole(m.Role),
                ["content"] = SerializeContent(m, enableMultimodal)
            };
            if (m.Role == ChatMessageRole.Tool && !string.IsNullOrWhiteSpace(m.ToolCallId))
                dict["tool_call_id"] = m.ToolCallId;
            if (m.ToolCalls is { Count: > 0 })
                dict["tool_calls"] = m.ToolCalls.Select(tc => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = tc.Id,
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = tc.Name,
                        ["arguments"] = tc.ArgumentsJson
                    }
                }).ToArray();
            return dict;
        }
    }

    private static Dictionary<string, object?> ToToolSchema(LlmToolSpec tool) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["type"] = "function",
        ["function"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = tool.Parameters ?? DefaultContainerSchema
        }
    };

    private static readonly JsonObject DefaultContainerSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["args"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" }
            },
            ["options"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = new JsonObject { ["type"] = "string" }
            }
        }
    };

    private HttpRequestMessage BuildRequest(
        IReadOnlyList<LocalChatMessage> messages, LlmOptions options, bool stream) =>
        new(HttpMethod.Post, BuildChatUrl())
        {
            Content = new StringContent(
                JsonSerializer.Serialize(BuildBody(messages, options, stream), HttpJsonOptions),
                Encoding.UTF8, "application/json")
        };

    private HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        if (!string.IsNullOrWhiteSpace(Config.ApiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Config.ApiKey);
        return http;
    }

    // ── Blocking completion ─────────────────────────────────────────────

    private Task<LlmChatResponse> CompleteAsync(
        IReadOnlyList<LocalChatMessage> messages, LlmOptions options, CancellationToken ct)
    {
        var enableMultimodal = options.EnableMultimodal && (Config.Capabilities?.SupportsVision ?? false);
        Info("llm", $"LLM request started: provider={Config.Provider}, model={Config.Model}, temp={options.Temperature}, messages={messages.Count}, multimodal={enableMultimodal}, tools={options.Tools?.Count ?? 0}");
        Debug("llm", $"LLM prompt: {JsonSerializer.Serialize(messages.Select(m => new { m.Role, Content = m.DebugContent }), LogOptions)}");

        return ExecuteWithRetryAsync("LLM request", async linked =>
        {
            using var http = CreateHttpClient();
            using var request = BuildRequest(messages, options, stream: false);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked);
            var body = await response.Content.ReadAsStringAsync(linked);
            var result = ExtractText(body);
            if (string.IsNullOrWhiteSpace(result.Content) && result.ToolCalls.Count == 0)
            {
                Warn("llm", $"LLM returned empty content. Raw response body: {body}");
                throw new InvalidOperationException("LLM returned empty content.");
            }
            Info("llm", $"LLM response (raw): {result.Content}");
            return result;
        }, ct);
    }

    // ── Streaming completion ────────────────────────────────────────────

    private Task<string> StreamAsync(
        IReadOnlyList<LocalChatMessage> messages, LlmOptions options, Action<string> onChunk, CancellationToken ct)
    {
        var enableMultimodal = options.EnableMultimodal && (Config.Capabilities?.SupportsVision ?? false);
        Info("llm", $"LLM streaming request started: provider={Config.Provider}, model={Config.Model}, temp={options.Temperature}, messages={messages.Count}, multimodal={enableMultimodal}");
        Debug("llm", $"LLM prompt: {JsonSerializer.Serialize(messages.Select(m => new { m.Role, Content = m.DebugContent }), LogOptions)}");

        return ExecuteWithRetryAsync("LLM streaming request", async linked =>
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
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var text = ExtractStreamChunk(line);
                if (string.IsNullOrEmpty(text)) continue;
                chunks.Add(text);
                onChunk(text);
            }

            var fullResponse = string.Concat(chunks);
            if (string.IsNullOrWhiteSpace(fullResponse))
                throw new InvalidOperationException("LLM streaming returned empty content.");
            Info("llm", $"LLM streaming response (raw): {fullResponse}");
            return fullResponse;
        }, ct);
    }

    // ── Response extraction ─────────────────────────────────────────────

    protected virtual LlmChatResponse ExtractText(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject();
        var message = node?["choices"]?[0]?["message"]?.AsObject();
        var content = message?["content"];
        var text = content switch
        {
            JsonValue value => value.ToString(),
            JsonArray parts => string.Concat(parts.Select(part => part?["text"]?.ToString() ?? part?["content"]?.ToString() ?? string.Empty)),
            _ => string.Empty
        };

        var toolCalls = new List<LlmToolCall>();
        if (message?["tool_calls"] is JsonArray calls)
        {
            foreach (var call in calls)
            {
                var function = call?["function"]?.AsObject();
                toolCalls.Add(new LlmToolCall
                {
                    Id = call?["id"]?.ToString() ?? "",
                    Name = function?["name"]?.ToString() ?? "",
                    ArgumentsJson = function?["arguments"]?.ToString() ?? ""
                });
            }
        }

        return new LlmChatResponse { Content = text, ToolCalls = toolCalls };
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

    // ── Content serialization ───────────────────────────────────────────

    private static object SerializeContent(LocalChatMessage message, bool enableMultimodal)
    {
        if (message.Blocks is not { Count: > 0 })
            return message.Content;

        if (!enableMultimodal)
            return message.DebugContent;

        var parts = message.Blocks
            .Select(BlockToContentPart)
            .Where(p => p is not null).Select(p => p!)
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

    private static string ResolveImageUrl(ImageBlock image)
    {
        if (!string.IsNullOrWhiteSpace(image.DataUri))
            return image.DataUri;

        if (image.AssetId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            image.AssetId.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            image.AssetId.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return image.AssetId;

        return $"asset://{image.AssetId}";
    }

    private static string ToProtocolRole(Llm.Core.Models.ChatMessageRole role) => role switch
    {
        Llm.Core.Models.ChatMessageRole.System => "system",
        Llm.Core.Models.ChatMessageRole.Assistant => "assistant",
        Llm.Core.Models.ChatMessageRole.Tool => "tool",
        _ => "user"
    };
}
