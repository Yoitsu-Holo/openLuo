namespace openLuo.Modules.Memory.Core.Models;

/// <summary>
/// Coarse emotional polarity attached to a memory record.
/// This is intentionally lightweight and is used for retrieval biasing rather than full affect simulation.
/// </summary>
public enum MemoryEmotion
{
    Neutral = 0,
    Positive = 1,
    Negative = 2,
    Mixed = 3
}
