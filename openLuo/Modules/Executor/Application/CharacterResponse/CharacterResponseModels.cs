using openLuo.Core.Models;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Executor.Application.CharacterResponse;

public sealed class CharacterResponseInput
{
    public string? SystemPromptOverride { get; init; }
    public string CharacterProfile { get; init; } = string.Empty;
    public string WorldContext { get; init; } = string.Empty;
    public string SceneState { get; init; } = string.Empty;
    public string GoalContext { get; init; } = string.Empty;
    public string LongTermMemory { get; init; } = string.Empty;
    public IReadOnlyList<string> ToolResults { get; init; } = [];
    public IReadOnlyList<CharacterResponseContextBlock> ExtraContexts { get; init; } = [];
    public IReadOnlyList<ChatMessage> Conversation { get; init; } = [];
    public string PlayerInput { get; init; } = string.Empty;
    public IReadOnlyList<Block>? PlayerBlocks { get; init; }
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public sealed class CharacterResponseContextBlock
{
    public EnhanceMessageRule Rule { get; init; }
    public string Content { get; init; } = string.Empty;
}
