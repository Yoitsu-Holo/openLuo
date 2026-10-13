namespace openLuo.Modules.Memory.Core.Models;

/// <summary>
/// Canonical structured memory entity used inside the rebuilt memory module.
/// It separates raw facts, summary text, retrieval text, and retrieval metadata.
/// </summary>
public sealed class MemoryRecord
{
    /// <summary>Stable memory identifier.</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>Game or save identifier.</summary>
    public string GameId { get; init; } = string.Empty;
    /// <summary>Owner character of this memory record.</summary>
    public string OwnerCharacterId { get; init; } = string.Empty;
    /// <summary>
    /// 说话人的稳定身份（平台用户 id，如 QQ 号）；空串 = 平台未提供，记忆不按用户分区。
    /// 隔离轴因此是 (game_id, character_id, user_id 非空?)：同角色面对不同用户时，
    /// 私有记忆互不可见，而未分区（空 user_id）的记忆对所有用户可见——这是老库/旧写入的语义。
    /// </summary>
    public string UserId { get; init; } = string.Empty;
    /// <summary>Visibility / ownership scope of the memory.</summary>
    public MemoryScope Scope { get; init; } = MemoryScope.CharacterPrivate;
    /// <summary>UTC occurrence time of the underlying event.</summary>
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    /// <summary>Raw event-like source text.</summary>
    public string SourceText { get; init; } = string.Empty;
    /// <summary>Compact summary intended for upper-layer prompt composition.</summary>
    public string Summary { get; init; } = string.Empty;
    /// <summary>Search-oriented text used by retrieval logic.</summary>
    public string RecallText { get; init; } = string.Empty;
    /// <summary>Lightweight retrieval tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>Optional extracted entities for later richer retrieval features.</summary>
    public IReadOnlyList<string> Entities { get; init; } = [];
    /// <summary>Coarse emotional signal of the memory.</summary>
    public MemoryEmotion Emotion { get; init; } = MemoryEmotion.Neutral;
    /// <summary>Importance score in the range [0, 1].</summary>
    public float Importance { get; init; }
    /// <summary>Retrieval salience score in the range [0, 1].</summary>
    public float Salience { get; init; }
    /// <summary>Arbitrary extension metadata kept opaque to the retrieval pipeline.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
