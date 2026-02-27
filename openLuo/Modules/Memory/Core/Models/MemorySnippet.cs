namespace openLuo.Modules.Memory.Core.Models;

/// <summary>
/// Compact memory view for upper layers that only need display-oriented recall snippets.
/// </summary>
public sealed class MemorySnippet
{
    public string MemoryId { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public MemoryScope Scope { get; init; } = MemoryScope.CharacterPrivate;
    public float? Score { get; init; }
}
