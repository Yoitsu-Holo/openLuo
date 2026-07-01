using openLuo.Core.Models;

namespace openLuo.Modules.Agent.Core.Interfaces;

/// <summary>
/// Resolves image asset references to data URIs suitable for direct LLM consumption.
/// </summary>
public interface IAssetImageResolver
{
    /// <summary>
    /// Resolves all ImageBlocks in the list, setting their DataUri property.
    /// Returns a new list with resolved blocks. Non-image blocks are passed through unchanged.
    /// </summary>
    Task<IReadOnlyList<Block>> ResolveAsync(IReadOnlyList<Block>? blocks, CancellationToken ct = default);
}
