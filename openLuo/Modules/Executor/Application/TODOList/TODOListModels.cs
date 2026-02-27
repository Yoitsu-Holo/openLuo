using System.Text.Json.Serialization;

namespace openLuo.Modules.Executor.Application.TODOList;

public sealed class TODOListInput
{
    public string? SystemPromptOverride { get; init; }
    public string CharacterProfile { get; init; } = string.Empty;
    public string WorldContext { get; init; } = string.Empty;
    public string SceneState { get; init; } = string.Empty;
    public string GoalContext { get; init; } = string.Empty;
    public string MemorySummary { get; init; } = string.Empty;
    public string CurrentStateSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> ToolCapabilities { get; init; } = [];
    public IReadOnlyList<string> Conversation { get; init; } = [];
    public string PlayerInput { get; init; } = string.Empty;
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public sealed class TODOListOutput
{
    [JsonPropertyName("todos")]
    public string[] Todos { get; init; } = [];
}
