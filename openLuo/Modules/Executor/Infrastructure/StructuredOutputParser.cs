using System.Text.Json;
using System.Text.RegularExpressions;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;

namespace openLuo.Modules.Executor.Infrastructure;

public sealed class StructuredOutputParser : IStructuredOutputParser
{
    private static readonly Regex JsonFenceRegex = new(
        @"```(?:json|JSON)?\s*(?<json>[\s\S]*?)\s*```",
        RegexOptions.Compiled);

    private readonly JsonSerializerOptions _jsonOptions;

    public StructuredOutputParser(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public StructuredParseResult<T> Parse<T>(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return StructuredParseResult<T>.Fail("Model output is empty.");

        var json = ExtractJson(raw);
        if (string.IsNullOrWhiteSpace(json))
            return StructuredParseResult<T>.Fail("No JSON object or array found in model output.");

        try
        {
            var value = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            return value is null
                ? StructuredParseResult<T>.Fail("JSON parsed to null.", json)
                : StructuredParseResult<T>.Ok(value, json);
        }
        catch (JsonException ex)
        {
            return StructuredParseResult<T>.Fail($"Invalid JSON output: {ex.Message}", json);
        }
    }

    private static string ExtractJson(string raw)
    {
        var trimmed = raw.Trim();
        var fenced = JsonFenceRegex.Match(trimmed);
        if (fenced.Success)
            return fenced.Groups["json"].Value.Trim();

        if (LooksLikeJson(trimmed))
            return trimmed;

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
            return trimmed[objectStart..(objectEnd + 1)].Trim();

        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
            return trimmed[arrayStart..(arrayEnd + 1)].Trim();

        return string.Empty;
    }

    private static bool LooksLikeJson(string value) =>
        (value.StartsWith('{') && value.EndsWith('}')) ||
        (value.StartsWith('[') && value.EndsWith(']'));
}
