using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

public interface IResourceEvaluationProjectionService
{
    Task<ResourceEvaluationSnapshot> BuildEvaluationSnapshotAsync(
        ResourceEvaluationQuery query,
        CancellationToken ct = default);
}
