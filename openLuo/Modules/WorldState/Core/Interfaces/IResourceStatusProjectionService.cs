using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

public interface IResourceStatusProjectionService
{
    Task<ResourceStatusSnapshot> BuildStatusSnapshotAsync(
        ResourceStatusQuery query,
        CancellationToken ct = default);
}
