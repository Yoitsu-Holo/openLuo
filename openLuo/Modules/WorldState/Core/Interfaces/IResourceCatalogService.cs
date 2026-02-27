using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

public interface IResourceCatalogService
{
    Task<IReadOnlyList<ResourceDefinitionView>> ListDefinitionsAsync(
        ResourceDefinitionQuery query,
        CancellationToken ct = default);

    Task<ResourceDefinitionView?> GetDefinitionAsync(
        string definitionId,
        CancellationToken ct = default);
}
