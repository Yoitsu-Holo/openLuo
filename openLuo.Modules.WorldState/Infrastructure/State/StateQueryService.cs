using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Infrastructure.State;

public class StateQueryService(IStateRegistry registry, IStateStore store) : IStateQueryService
{
    public async Task<StateValue> GetAsync(string gameId, string @namespace, StateOwnerKind ownerKind, string ownerId, string key)
    {
        var raw = await store.GetRawAsync(gameId, ownerKind, ownerId, @namespace, key);
        if (raw is not null)
        {
            return new StateValue
            {
                GameId = gameId,
                Namespace = @namespace,
                Key = key,
                OwnerKind = ownerKind,
                OwnerId = ownerId,
                Value = raw,
                Defaulted = false
            };
        }

        var def = registry.GetDef(@namespace, ownerKind, key);
        return new StateValue
        {
            GameId = gameId,
            Namespace = @namespace,
            Key = key,
            OwnerKind = ownerKind,
            OwnerId = ownerId,
            Value = def?.DefaultValue ?? string.Empty,
            Defaulted = true
        };
    }

    public async Task<List<StateValue>> QueryAsync(string gameId, string? @namespace, StateOwnerKind? ownerKind, string? ownerId, IEnumerable<string>? keys = null, bool includeDefaults = false)
    {
        var stored = await store.QueryAsync(gameId, ownerKind, ownerId, @namespace, keys);

        if (!includeDefaults)
            return stored;

        // Build a set of already-stored keys to avoid duplicates
        var storedKeys = stored
            .Select(v => $"{v.Namespace}:{v.OwnerKind}:{v.OwnerId}:{v.Key}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var keysList = keys?.ToList();
        var allDefs = registry.GetAllDefs();

        // Filter defs by provided criteria
        if (@namespace is not null)
            allDefs = allDefs.Where(d => d.Namespace.Equals(@namespace, StringComparison.OrdinalIgnoreCase));
        if (ownerKind.HasValue)
            allDefs = allDefs.Where(d => d.OwnerKind == ownerKind.Value);
        if (keysList is { Count: > 0 })
            allDefs = allDefs.Where(d => keysList.Contains(d.Key, StringComparer.OrdinalIgnoreCase));

        foreach (var def in allDefs)
        {
            // ownerId to use for this def: if caller supplied one, use it; otherwise skip (can't infer owner)
            if (ownerId is null) continue;

            var compositeKey = $"{def.Namespace}:{def.OwnerKind}:{ownerId}:{def.Key}";
            if (storedKeys.Contains(compositeKey)) continue;

            stored.Add(new StateValue
            {
                GameId = gameId,
                Namespace = def.Namespace,
                Key = def.Key,
                OwnerKind = def.OwnerKind,
                OwnerId = ownerId,
                Value = def.DefaultValue ?? string.Empty,
                Defaulted = true
            });
        }

        return stored;
    }
}
