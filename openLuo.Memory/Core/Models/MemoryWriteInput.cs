namespace openLuo.Modules.Memory.Core.Models;

/// <summary>
/// External write-side input accepted by the memory module.
/// This stays intentionally simple and event-like so upper layers do not construct storage models directly.
/// </summary>
public sealed class MemoryWriteInput
{
    /// <summary>Game or save identifier.</summary>
    public string GameId { get; init; } = string.Empty;
    /// <summary>Owner character of the new memory.</summary>
    public string CharacterId { get; init; } = string.Empty;
    /// <summary>Requested visibility scope.</summary>
    public MemoryScope Scope { get; init; } = MemoryScope.CharacterPrivate;
    /// <summary>Raw event content provided by the caller.</summary>
    public string RawContent { get; init; } = string.Empty;
    /// <summary>Occurrence time in UTC.</summary>
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    /// <summary>Free-form source label such as demo, executor, gift, etc.</summary>
    public string Source { get; init; } = string.Empty;
    /// <summary>Coarse emotional polarity.</summary>
    public MemoryEmotion Emotion { get; init; } = MemoryEmotion.Neutral;
    /// <summary>Importance hint in the range [0, 1].</summary>
    public float Importance { get; init; } = 0.5f;
    /// <summary>Opaque extension metadata for future use.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
