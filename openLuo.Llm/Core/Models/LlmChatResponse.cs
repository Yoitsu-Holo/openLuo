namespace openLuo.Modules.Llm.Core.Models;

/// <summary>
/// Result of a non-streaming chat completion: reply text plus any tool calls the model requested.
/// </summary>
public sealed class LlmChatResponse
{
    public string Content { get; init; } = "";
    public IReadOnlyList<LlmToolCall> ToolCalls { get; init; } = [];
}
