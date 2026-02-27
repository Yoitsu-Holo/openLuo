using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Agent.Infrastructure;

public sealed class AgentMemoryStoreAdapter : IAgentMemoryStore
{
    private readonly IMemoryWriteService _writeService;
    private readonly IMemoryRecallService _recallService;

    public AgentMemoryStoreAdapter(IMemoryWriteService writeService, IMemoryRecallService recallService)
    {
        _writeService = writeService;
        _recallService = recallService;
    }

    public async Task StoreAgentEventAsync(string gameId, string characterId, string content, int emotionalWeight, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameId) ||
            string.IsNullOrWhiteSpace(characterId) ||
            string.IsNullOrWhiteSpace(content))
            return;

        await _writeService.WriteAsync(new MemoryWriteInput
        {
            GameId = gameId,
            CharacterId = characterId,
            Scope = MemoryScope.CharacterPrivate,
            RawContent = content.Trim(),
            Source = "agent/runtime",
            Emotion = emotionalWeight switch
            {
                > 1 => MemoryEmotion.Positive,
                < 0 => MemoryEmotion.Negative,
                _ => MemoryEmotion.Neutral
            },
            Importance = Math.Clamp(0.35f + Math.Abs(emotionalWeight) * 0.15f, 0.1f, 1.0f),
            Metadata = new Dictionary<string, string>
            {
                ["source_module"] = "Agent",
                ["emotional_weight"] = emotionalWeight.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        }, ct);
    }

    public async Task<IReadOnlyList<MemoryRecord>> RecallSharedAsync(string query, string gameId, string? excludeCharacterId, int topK, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(query) || topK <= 0)
            return [];

        var result = await _recallService.RecallAsync(new SemanticRecallQuery
        {
            GameId = gameId,
            CharacterId = excludeCharacterId ?? string.Empty,
            SearchText = query.Trim(),
            Scopes = [MemoryScope.Shared],
            TopK = topK,
            Reason = "agent/shared"
        }, ct);

        return result.Records;
    }

    public async Task<IReadOnlyList<MemoryRecord>> GetRecentPrivateAsync(string gameId, string characterId, int count, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(characterId) || count <= 0)
            return [];

        var result = await _recallService.RecallAsync(new SemanticRecallQuery
        {
            GameId = gameId,
            CharacterId = characterId,
            SearchText = characterId,
            QueryTags = [characterId, "recent", "agent"],
            Scopes = [MemoryScope.CharacterPrivate],
            PreferRecent = true,
            TopK = count,
            Reason = "agent/private-recent"
        }, ct);

        return result.Records;
    }
}
