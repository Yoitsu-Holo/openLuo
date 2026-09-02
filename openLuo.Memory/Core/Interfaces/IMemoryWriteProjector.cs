using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Core.Interfaces;

/// <summary>
/// Converts an external write input into the internal structured semantic memory record.
/// </summary>
public interface IMemoryWriteProjector
{
    /// <summary>
    /// Project a raw write request into a normalized memory record.
    /// </summary>
    Task<MemoryRecord> ProjectAsync(MemoryWriteInput input, CancellationToken ct = default);
}
