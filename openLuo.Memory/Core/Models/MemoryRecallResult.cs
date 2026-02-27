namespace openLuo.Modules.Memory.Core.Models;

/// <summary>
/// Result returned by the semantic memory recall pipeline.
/// </summary>
public sealed class MemoryRecallResult
{
    /// <summary>
    /// Whether the retrieval pipeline completed successfully.
    /// A degraded lexical fallback may still return <c>true</c>.
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// Normalized structured records returned by the retrieval pipeline.
    /// </summary>
    public IReadOnlyList<MemoryRecord> Records { get; init; } = [];

    /// <summary>
    /// Optional preformatted recall summary for higher layers that only need a compact block.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Retrieval trace lines for debugging which strategy was used.
    /// </summary>
    public IReadOnlyList<string> Trace { get; init; } = [];

    /// <summary>
    /// True when the pipeline had to fall back from the preferred retrieval path.
    /// </summary>
    public bool Degraded { get; init; }
}
