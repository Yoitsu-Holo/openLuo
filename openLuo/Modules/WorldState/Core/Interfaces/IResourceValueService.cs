using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

public interface IResourceValueService
{
    Task<IReadOnlyList<ResourceValueView>> QueryValuesAsync(
        ResourceValueQuery query,
        CancellationToken ct = default);
}
