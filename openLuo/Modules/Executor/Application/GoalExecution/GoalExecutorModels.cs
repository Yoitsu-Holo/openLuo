using System.Text.Json.Serialization;

namespace openLuo.Modules.Executor.Application.GoalExecution;

public sealed class GoalExecutorInput
{
    public string? SystemPromptOverride { get; init; }
    public string CharacterProfile { get; init; } = string.Empty;
    public string SceneState { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
    public IReadOnlyList<GoalExecutorCapability> AvailableTools { get; init; } = [];
    public IReadOnlyList<string> ToolExecutionHistory { get; init; } = [];
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public sealed class GoalExecutorCapability
{
    public string Name { get; init; } = string.Empty;
    public string Help { get; init; } = string.Empty;
    public string Usage { get; init; } = string.Empty;
}

public sealed class GoalExecutorOutput
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
