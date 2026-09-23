using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace openLuo.Infrastructure.Json;

/// <summary>
/// JSON 容错解析器。支持从 markdown fence (```json...```) 中提取 JSON。
/// </summary>
public static class JsonHelper
{
    private static readonly Regex JsonFenceRegex = new(
        @"```(?:json|JSON)?\s*(?<json>[\s\S]*?)\s*```",
        RegexOptions.Compiled);

    /// <summary>
    /// 尝试解析字符串为 JSON，兼容 ```json ... ``` fence 包裹。
    /// 解析失败时返回 <paramref name="fallback"/>。
    /// </summary>
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
