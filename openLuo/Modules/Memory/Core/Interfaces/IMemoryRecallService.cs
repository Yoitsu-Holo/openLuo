using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Core.Interfaces;

/// <summary>
/// Semantic read-side entry of the memory module.
/// Accepts a structured recall query and returns normalized memory records.
/// </summary>
public interface IMemoryRecallService
{
    /// <summary>
    /// Recall memories for the given semantic query.
    /// The caller does not care whether the module uses vector search or lexical fallback.
    /// </summary>
    Task<MemoryRecallResult> RecallAsync(SemanticRecallQuery query, CancellationToken ct = default);
}
