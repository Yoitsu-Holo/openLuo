using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Core.Interfaces;

/// <summary>
/// Semantic write-side entry of the memory module.
/// Accepts raw event-like input and hides projection, persistence, and optional embedding generation.
/// </summary>
public interface IMemoryWriteService
{
    /// <summary>
    /// Write a semantic memory entry.
    /// </summary>
    Task<MemoryWriteResult> WriteAsync(MemoryWriteInput input, CancellationToken ct = default);
}
