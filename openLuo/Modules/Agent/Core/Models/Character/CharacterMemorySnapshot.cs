using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterMemorySnapshot
{
    public IReadOnlyList<MemoryRecord> RecentPrivateMemories { get; init; } = [];
    public IReadOnlyList<MemoryRecord> SharedMemories { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}
