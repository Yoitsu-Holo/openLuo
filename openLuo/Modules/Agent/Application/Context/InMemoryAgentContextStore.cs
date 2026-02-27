using System.Collections.Concurrent;

namespace openLuo.Modules.Agent.Application;

public sealed class InMemoryAgentContextStore : IAgentContextStore
{
    private readonly ConcurrentDictionary<string, AgentContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

    public Task<AgentContext> GetOrCreateAsync(string gameId, string characterId, CancellationToken ct = default)
    {
        var key = $"{gameId}::{characterId}".ToLowerInvariant();
        var context = _contexts.GetOrAdd(key, _ => new AgentContext
        {
            GameId = gameId,
            CharacterId = characterId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        return Task.FromResult(context);
    }

    public Task SaveAsync(AgentContext context, CancellationToken ct = default)
    {
        var key = $"{context.GameId}::{context.CharacterId}".ToLowerInvariant();
        _contexts[key] = context;
        return Task.CompletedTask;
    }
}
