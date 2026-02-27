using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

public interface IResourceLifecycleService
{
    Task<ResourceDefinitionView?> UpdateLifecycleAsync(
        ResourceLifecycleUpdate update,
        CancellationToken ct = default);
}
