using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Application;

/// <summary>
/// Application-layer read coordinator of the memory module.
/// Delegates retrieval to the configured retrieval strategy stack.
/// </summary>
public sealed class MemoryRecallCoordinator : IMemoryRecallService
{
    private readonly IMemoryRetriever _retriever;

    public MemoryRecallCoordinator(IMemoryRetriever retriever)
    {
        _retriever = retriever;
    }

    public Task<MemoryRecallResult> RecallAsync(SemanticRecallQuery query, CancellationToken ct = default) =>
        _retriever.RetrieveAsync(query, ct);
}
