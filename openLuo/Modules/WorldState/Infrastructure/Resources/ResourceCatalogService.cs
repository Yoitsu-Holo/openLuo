using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Infrastructure.Resources;

public sealed class ResourceCatalogService(IStateRegistry registry) : IResourceCatalogService
{
    public Task<IReadOnlyList<ResourceDefinitionView>> ListDefinitionsAsync(
        ResourceDefinitionQuery query,
        CancellationToken ct = default)
    {
        IEnumerable<StateDef> defs = registry.GetAllDefs();

        if (!string.IsNullOrWhiteSpace(query.Namespace))
            defs = defs.Where(def => def.Namespace.Equals(query.Namespace, StringComparison.OrdinalIgnoreCase));

        if (query.OwnerKind.HasValue)
            defs = defs.Where(def => def.OwnerKind == query.OwnerKind.Value);

        if (!string.IsNullOrWhiteSpace(query.PluginId))
            defs = defs.Where(def => string.Equals(def.PluginId, query.PluginId, StringComparison.OrdinalIgnoreCase));

        if (query.LifecycleState.HasValue)
            defs = defs.Where(def => def.LifecycleState == query.LifecycleState.Value);
        else if (!query.IncludeRetired)
            defs = defs.Where(def => def.LifecycleState != ResourceLifecycleState.Retired);

        if (query.VisibleInStatusOnly)
            defs = defs.Where(def => !def.HiddenFromStatus && def.LifecycleState is ResourceLifecycleState.Active or ResourceLifecycleState.Frozen);

        if (query.MutableByLlmOnly)
            defs = defs.Where(def => def.MutableByLlm && def.LifecycleState is ResourceLifecycleState.Active or ResourceLifecycleState.Hidden);

        IReadOnlyList<ResourceDefinitionView> result = defs
            .OrderBy(def => def.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(def => def.OwnerKind.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(def => def.StatusGroup ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(def => def.StatusOrder)
            .ThenBy(def => def.Key, StringComparer.OrdinalIgnoreCase)
            .Select(ResourceViewMapper.ToView)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<ResourceDefinitionView?> GetDefinitionAsync(
        string definitionId,
        CancellationToken ct = default)
    {
        var def = registry.GetAllDefs()
            .FirstOrDefault(candidate => candidate.DefinitionId.Equals(definitionId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(def is null ? null : ResourceViewMapper.ToView(def));
    }
}
