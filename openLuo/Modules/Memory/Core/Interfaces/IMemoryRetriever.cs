using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Core.Interfaces;

/// <summary>
/// Retrieval strategy boundary of the memory module.
/// Implementations may use vector search, lexical search, or compose multiple strategies.
/// </summary>
public interface IMemoryRetriever
{
    /// <summary>
    /// Retrieve candidate memories for the given semantic query.
    /// </summary>
    Task<MemoryRecallResult> RetrieveAsync(SemanticRecallQuery query, CancellationToken ct = default);
}
