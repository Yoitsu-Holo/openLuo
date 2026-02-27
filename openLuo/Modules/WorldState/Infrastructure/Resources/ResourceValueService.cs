using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Infrastructure.Resources;

public sealed class ResourceValueService(
    IResourceCatalogService catalog,
    IStateQueryService stateQueryService) : IResourceValueService
{
    public async Task<IReadOnlyList<ResourceValueView>> QueryValuesAsync(
        ResourceValueQuery query,
        CancellationToken ct = default)
    {
        var defs = await catalog.ListDefinitionsAsync(new ResourceDefinitionQuery
        {
            Namespace = query.Namespace,
            OwnerKind = query.OwnerKind
        }, ct);

        if (query.Keys is { Count: > 0 })
            defs = defs.Where(def => query.Keys.Contains(def.Key, StringComparer.OrdinalIgnoreCase)).ToArray();

        if (query.OwnerKind.HasValue || !string.IsNullOrWhiteSpace(query.OwnerId))
        {
            var raw = await stateQueryService.QueryAsync(
                query.GameId,
                query.Namespace,
                query.OwnerKind,
                query.OwnerId,
                query.Keys,
                query.IncludeDefaults);

            return raw.Select(value => ToView(value, defs)).Where(value => value is not null).Cast<ResourceValueView>().ToArray();
        }

        var values = new List<ResourceValueView>();
        foreach (var def in defs)
        {
            var ownerId = ResolveOwnerId(query.GameId, def, query.OwnerId);
            var value = await stateQueryService.GetAsync(query.GameId, def.Namespace, def.OwnerKind, ownerId, def.Key);
            values.Add(ToView(value, [def])!);
        }

        return values;
    }

    private static ResourceValueView? ToView(StateValue value, IReadOnlyList<ResourceDefinitionView> defs)
    {
        var def = defs.FirstOrDefault(candidate =>
            candidate.Namespace.Equals(value.Namespace, StringComparison.OrdinalIgnoreCase) &&
            candidate.Key.Equals(value.Key, StringComparison.OrdinalIgnoreCase) &&
            candidate.OwnerKind == value.OwnerKind);

        if (def is null)
            return null;

        return new ResourceValueView
        {
            Definition = def,
            GameId = value.GameId,
            OwnerId = value.OwnerId,
            Value = value.Value,
            Defaulted = value.Defaulted,
            UpdatedAt = value.UpdatedAt
        };
    }

    private static string ResolveOwnerId(string gameId, ResourceDefinitionView def, string? ownerId)
    {
        if (!string.IsNullOrWhiteSpace(ownerId))
            return ownerId;

        return def.OwnerKind switch
        {
            StateOwnerKind.Game => gameId,
            StateOwnerKind.System => "system",
            _ => string.Empty
        };
    }
}
