namespace openLuo.Modules.Llm.Core.Models;

/// <summary>
/// Represents a structured context block that can be converted into a standard chat message.
/// </summary>
/// <param name="Role">Message role.</param>
/// <param name="Rule">Structured block kind, for example <see cref="EnhanceMessageRule.CharacterProfile"/>.</param>
/// <param name="Content">Structured block content.</param>
public record EnhanceMessage(ChatMessageRole Role, EnhanceMessageRule Rule, string Content)
{
    public ChatMessage ToChatMessage() => new(Role, WrapContent(Rule, Content));

    public static implicit operator ChatMessage(EnhanceMessage message) => message.ToChatMessage();

    private static string WrapContent(EnhanceMessageRule rule, string content)
    {
        var normalizedRule = rule.ToProtocolString();
        return $"[{normalizedRule}]\n{content}\n[/{normalizedRule}]";
    }
}
