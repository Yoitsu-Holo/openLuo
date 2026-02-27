using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Executor.Core.Models;

public sealed class ExecutorPrompt
{
    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];
    public LlmOptions Options { get; init; } = new();
}
