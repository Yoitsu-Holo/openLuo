namespace openLuo.Modules.Agent.Application;

public sealed class AgentConversationTurn
{
    public string SpeakerId { get; set; } = string.Empty;
    public string SpeakerRole { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgentContext
{
    public string GameId { get; set; } = string.Empty;
    public string CharacterId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<AgentConversationTurn> Conversation { get; } = [];
}

public interface IAgentContextStore
{
    Task<AgentContext> GetOrCreateAsync(string gameId, string characterId, CancellationToken ct = default);

    Task SaveAsync(AgentContext context, CancellationToken ct = default);
}
