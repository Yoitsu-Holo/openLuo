namespace openLuo.Modules.Llm.Core.Models;

/// <summary>
/// Represents a system message with an implicit <see cref="ChatMessageRole.System"/> role.
/// </summary>
/// <param name="Content">System prompt text content.</param>
public record SystemMessage(string Content)
{
    public ChatMessage ToChatMessage() => new(ChatMessageRole.System, Content);

    public static implicit operator ChatMessage(SystemMessage message) => message.ToChatMessage();
}
