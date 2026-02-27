namespace openLuo.Modules.Agent.Application;

public sealed class CharacterAgentState
{
    public string GameId { get; init; } = string.Empty;
    public string CharacterId { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<AgentConversationTurn> Conversation { get; init; } = [];
}
