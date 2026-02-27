using System.Text.Json.Serialization;

namespace openLuo.Modules.PluginRuntime.Core.Models.HookContexts;

public sealed class OnToolExecutedInput : HookContext
{
    [JsonPropertyName("characterId")]
    public string? CharacterId { get; set; }

    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("executorKind")]
    public string? ExecutorKind { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("args")]
    public IReadOnlyList<string> Args { get; set; } = [];

    [JsonPropertyName("options")]
    public IReadOnlyDictionary<string, string> Options { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("outputText")]
    public string? OutputText { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }

    [JsonPropertyName("assetIds")]
    public IReadOnlyList<string> AssetIds { get; set; } = [];

    [JsonPropertyName("mimeTypes")]
    public IReadOnlyList<string> MimeTypes { get; set; } = [];
}
