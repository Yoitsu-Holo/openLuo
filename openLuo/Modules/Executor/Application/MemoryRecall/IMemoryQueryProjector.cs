using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Executor.Application.MemoryRecall;

public interface IMemoryQueryProjector
{
    Task<SemanticRecallQuery> ProjectAsync(
        MemoryRecallInput input,
        CancellationToken ct = default);
}
