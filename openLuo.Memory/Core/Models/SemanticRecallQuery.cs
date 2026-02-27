namespace openLuo.Modules.Memory.Core.Models;

/// <summary>
/// Structured query passed into the semantic recall pipeline.
/// </summary>
public sealed class SemanticRecallQuery
{
    /// <summary>Game or save identifier.</summary>
    public string GameId { get; init; } = string.Empty;
    /// <summary>Current querying character.</summary>
    public string CharacterId { get; init; } = string.Empty;
    /// <summary>Main natural language search text.</summary>
    public string SearchText { get; init; } = string.Empty;
    /// <summary>Auxiliary query tags extracted by upper layers.</summary>
    public IReadOnlyList<string> QueryTags { get; init; } = [];
    /// <summary>Scopes allowed for this recall request.</summary>
    public IReadOnlyList<MemoryScope> Scopes { get; init; } = [];
    /// <summary>Whether recent memories should be slightly preferred.</summary>
    public bool PreferRecent { get; init; } = true;
    /// <summary>Whether high-importance memories should be slightly preferred.</summary>
    public bool PreferImportant { get; init; } = true;
    /// <summary>Whether emotionally salient memories should be slightly preferred.</summary>
    public bool PreferEmotionallySalient { get; init; } = true;
    /// <summary>Maximum number of returned records.</summary>
    public int TopK { get; init; } = 5;
    /// <summary>Optional minimum importance filter.</summary>
    public float? MinImportance { get; init; }
    /// <summary>Human-readable reason used for debugging or traceability.</summary>
    public string Reason { get; init; } = string.Empty;
}
