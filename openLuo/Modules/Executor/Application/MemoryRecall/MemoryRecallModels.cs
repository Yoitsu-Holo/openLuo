using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Executor.Application.MemoryRecall;

public sealed class MemoryRecallInput
{
    public string GameId { get; init; } = string.Empty;
    public string CharacterId { get; init; } = string.Empty;
    public string PlayerInput { get; init; } = string.Empty;
    public string SceneState { get; init; } = string.Empty;
    public string CurrentGoal { get; init; } = string.Empty;
    public IReadOnlyList<ChatMessage> RecentConversation { get; init; } = [];
    public MemoryRecallOptions Options { get; init; } = new();
}

public sealed class MemoryRecallOptions
{
    public int TopK { get; init; } = 5;
    public bool IncludeCharacterPrivateMemory { get; init; } = true;
    public bool IncludeSharedMemory { get; init; }
    public bool PreferRecent { get; init; } = true;
    public bool PreferImportant { get; init; } = true;
    public bool PreferEmotionallySalient { get; init; } = true;
    public int MaxSnippetCount { get; init; } = 4;
    public int MaxSummaryChars { get; init; } = 600;
}

public sealed class MemoryRecallFormattedResult
{
    public IReadOnlyList<MemorySnippet> Snippets { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}

public sealed class MemoryRecallOutput
{
    public SemanticRecallQuery Query { get; init; } = new();
    public IReadOnlyList<MemorySnippet> MemorySnippets { get; init; } = [];
    public string MemorySummary { get; init; } = string.Empty;
    public IReadOnlyList<string> RetrievalTrace { get; init; } = [];
    public bool Degraded { get; init; }
}
