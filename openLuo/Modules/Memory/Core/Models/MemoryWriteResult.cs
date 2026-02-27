namespace openLuo.Modules.Memory.Core.Models;

/// <summary>
/// Result returned by the semantic memory write pipeline.
/// </summary>
public sealed class MemoryWriteResult
{
    /// <summary>Whether the write pipeline completed successfully.</summary>
    public bool Success { get; init; }
    /// <summary>Identifier of the stored memory record.</summary>
    public string MemoryId { get; init; } = string.Empty;
    /// <summary>Stored scope after the write pipeline completed.</summary>
    public MemoryScope StoredScope { get; init; } = MemoryScope.CharacterPrivate;
    /// <summary>Write completion timestamp in UTC.</summary>
    public DateTime StoredAtUtc { get; init; } = DateTime.UtcNow;
    /// <summary>Trace lines describing projection / persistence / embedding steps.</summary>
    public IReadOnlyList<string> Trace { get; init; } = [];
}
