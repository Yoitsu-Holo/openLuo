namespace openLuo.Modules.Llm.Core.Models;

/// <summary>
/// A tool call requested by the model in a chat response (OpenAI-compatible
/// <c>choices[0].message.tool_calls</c> entry).
/// </summary>
public sealed class LlmToolCall
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ArgumentsJson { get; init; } = "";
}
