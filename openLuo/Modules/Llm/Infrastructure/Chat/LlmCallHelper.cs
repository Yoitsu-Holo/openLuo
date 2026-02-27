using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using openLuo.Core.Interfaces;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Llm.Infrastructure.Chat;

public static class LlmCallHelper
{
    private static readonly Regex JsonFenceRegex = new(
        @"```(?:json|JSON)?\s*(?<json>[\s\S]*?)\s*```",
        RegexOptions.Compiled);

    public static async Task<string> CallWithLoggingAsync(
        ILlmClient client,
        IGameLogger? logger,
        string category,
        string prompt,
        LlmOptions? options = null,
        Action<string>? onChunk = null)
    {
        logger?.Info(category, $"llm start promptLen={prompt.Length}");
        var messages = new[] { new ChatMessage(ChatMessageRole.User, prompt) };
        var raw = onChunk is null
            ? await client.CompleteAsync(messages, options)
            : await client.StreamAsync(messages, onChunk, options);
        logger?.Info(category, $"llm done replyLen={raw.Length}");
        return raw;
    }

    public static JsonNode? ParseJsonResponse(string raw, JsonNode? fallback = null)
    {
        try
        {
            var trimmed = raw.Trim();
            var fenced = JsonFenceRegex.Match(trimmed);
            var candidate = fenced.Success ? fenced.Groups["json"].Value.Trim() : trimmed;
            return JsonNode.Parse(candidate);
        }
        catch
        {
            return fallback;
        }
    }
}
