namespace openLuo.Modules.Agent.Application;

public sealed class CharacterPromptContext
{
    public string CharacterProfile { get; init; } = string.Empty;
    public string WorldContext { get; init; } = string.Empty;
    public string SceneState { get; init; } = string.Empty;
    public string GoalContext { get; init; } = string.Empty;
    public string CurrentStateSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> AvailableTools { get; init; } = [];
    public IReadOnlyList<string> ToolCatalog { get; init; } = [];
    public IReadOnlyList<AgentConversationMessage> Conversation { get; init; } = [];
    public IReadOnlyList<AgentContextBlock> ExtraContexts { get; init; } = [];
    public string PlayerInput { get; init; } = string.Empty;
}
