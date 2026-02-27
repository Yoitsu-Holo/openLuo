using System.Text.Json.Serialization;

namespace openLuo.Modules.Executor.Application.ToolUse;

public sealed class ToolUseInput
{
    public string? SystemPromptOverride { get; init; }
    public string CharacterProfile { get; init; } = string.Empty;
    public string SceneState { get; init; } = string.Empty;
    public string CurrentGoal { get; init; } = string.Empty;
    public string PlanSummary { get; init; } = string.Empty;
    public IReadOnlyList<ToolUseCapability> AvailableTools { get; init; } = [];
    public IReadOnlyList<string> Conversation { get; init; } = [];
    public string PlayerInput { get; init; } = string.Empty;
    public string LastToolResult { get; init; } = string.Empty;
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public sealed class ToolUseCapability
{
    public string Name { get; init; } = string.Empty;
    public string Help { get; init; } = string.Empty;
    public string Usage { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = "low";
    public bool NeedsConfirm { get; init; }
}

public sealed class ToolUseOutput
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = "none";

    [JsonPropertyName("toolName")]
    public string ToolName { get; init; } = string.Empty;

    [JsonPropertyName("args")]
    public string[] Args { get; init; } = [];

    [JsonPropertyName("options")]
    public Dictionary<string, string> Options { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}
