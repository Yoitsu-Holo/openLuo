using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

/// <summary>Queries state values with optional filtering.</summary>
public interface IStateQueryService
{
    /// <summary>Get a single state value (returns default if not persisted).</summary>
    Task<StateValue> GetAsync(string gameId, string @namespace, StateOwnerKind ownerKind, string ownerId, string key);

    /// <summary>Query multiple state values by filter criteria.</summary>
    Task<List<StateValue>> QueryAsync(string gameId, string? @namespace, StateOwnerKind? ownerKind, string? ownerId, IEnumerable<string>? keys = null, bool includeDefaults = false);
}
