using System.Globalization;
using System.Text.Json.Nodes;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Infrastructure.Resources;

public sealed class ResourceEvaluationProjectionService(
    IResourceCatalogService catalog,
    IStateQueryService stateQueryService) : IResourceEvaluationProjectionService
{
    public async Task<ResourceEvaluationSnapshot> BuildEvaluationSnapshotAsync(
        ResourceEvaluationQuery query,
        CancellationToken ct = default)
    {
        var defs = await catalog.ListDefinitionsAsync(new ResourceDefinitionQuery
        {
            IncludeRetired = false
        }, ct);

        var targetDefs = defs
            .Where(def => def.OwnerKind is StateOwnerKind.Character or StateOwnerKind.Game)
            .Where(def => def.LifecycleState is ResourceLifecycleState.Active or ResourceLifecycleState.Hidden)
            .ToArray();

        var snapshot = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ResourceEvaluationItemView>();

        foreach (var def in targetDefs)
        {
            var ownerId = ResolveOwnerId(query.GameId, query.CharacterId, def.OwnerKind);
            if (string.IsNullOrWhiteSpace(ownerId))
                continue;

            var value = await stateQueryService.GetAsync(query.GameId, def.Namespace, def.OwnerKind, ownerId, def.Key);
            var nsKey = ToCamelCase(def.Namespace);
            if (!snapshot.TryGetValue(nsKey, out var nsValues))
            {
                nsValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                snapshot[nsKey] = nsValues;
            }

            nsValues[def.Key] = value.Value;

            if (!query.IncludeReadOnlyContext && (!def.MutableByLlm || def.Derived))
                continue;

            items.Add(new ResourceEvaluationItemView
            {
                Definition = def,
                OwnerId = ownerId,
                ResourceId = def.Key,
                Value = value.Value,
                Defaulted = value.Defaulted,
                MaxDeltaPerTurn = ReadMaxDeltaPerTurn(def.Metadata)
            });
        }

        return new ResourceEvaluationSnapshot
        {
            GameId = query.GameId,
            CharacterId = query.CharacterId,
            ArchetypeId = query.ArchetypeId,
            Items = items,
            StateSnapshot = snapshot
        };
    }

    private static string ResolveOwnerId(string gameId, string characterId, StateOwnerKind ownerKind) =>
        ownerKind switch
        {
            StateOwnerKind.Game => gameId,
            StateOwnerKind.Character => characterId,
            _ => string.Empty
        };

    private static double? ReadMaxDeltaPerTurn(JsonObject? metadata)
    {
        var raw = metadata?["maxDeltaPerTurn"]?.ToString();
        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string ToCamelCase(string snakeCase)
    {
        var parts = snakeCase.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return snakeCase;

        return parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
