using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.WorldState.Infrastructure.State;

namespace openLuo.Modules.WorldState.Infrastructure.Resources;

public sealed class ResourceLifecycleService(
    IStateRegistry registry,
    StateDefStore defStore,
    IResourceCatalogService catalog) : IResourceLifecycleService
{
    public async Task<ResourceDefinitionView?> UpdateLifecycleAsync(
        ResourceLifecycleUpdate update,
        CancellationToken ct = default)
    {
        var def = registry.GetAllDefs()
            .FirstOrDefault(candidate => candidate.DefinitionId.Equals(update.DefinitionId, StringComparison.OrdinalIgnoreCase));
        if (def is null)
            return null;

        def.LifecycleState = update.LifecycleState;
        def.RetirementPolicy = update.RetirementPolicy;
        registry.Register(def);
        defStore.UpdateLifecycle(def.DefinitionId, update.LifecycleState, update.RetirementPolicy);

        return await catalog.GetDefinitionAsync(def.DefinitionId, ct);
    }
}
