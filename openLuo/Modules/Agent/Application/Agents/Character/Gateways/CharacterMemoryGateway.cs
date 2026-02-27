using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterMemoryGateway : ICharacterMemoryGateway
{
    private readonly IAgentMemoryStore _memoryStore;

    public CharacterMemoryGateway(IAgentMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    public async Task<CharacterMemorySnapshot> LoadAsync(AgentContext context, AgentMessage message, CancellationToken ct = default)
    {
        var recentPrivate = CharacterTurnPolicy.ShouldUsePrivateMemory(message.Type)
            ? await _memoryStore.GetRecentPrivateAsync(context.GameId, context.CharacterId, 3, ct)
            : Array.Empty<MemoryRecord>();
        var shared = CharacterTurnPolicy.ShouldUseSharedMemory(message.Type)
            ? await _memoryStore.RecallSharedAsync(message.Payload, context.GameId, context.CharacterId, 2, ct)
            : Array.Empty<MemoryRecord>();

        return new CharacterMemorySnapshot
        {
            RecentPrivateMemories = recentPrivate,
            SharedMemories = shared,
            Summary = BuildSummary(recentPrivate, shared)
        };
    }

    private static string BuildSummary(IReadOnlyList<MemoryRecord> recentPrivate, IReadOnlyList<MemoryRecord> shared)
    {
        // TODO: 当前召回机制有问题（仅存玩家单侧输入、无摘要、无语义关联），暂返回空。
        // 等 MemoryRecall 链路修好后再恢复。
        return string.Empty;
    }

    private static string PickText(MemoryRecord memory)
    {
        if (!string.IsNullOrWhiteSpace(memory.Summary))
            return memory.Summary;
        if (!string.IsNullOrWhiteSpace(memory.RecallText))
            return memory.RecallText;
        return memory.SourceText;
    }
}
