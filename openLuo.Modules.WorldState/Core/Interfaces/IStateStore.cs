using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

/// <summary>Handles database-level read/write for state values.</summary>
public interface IStateStore
{
    /// <summary>Get a single state value, or null if not persisted.</summary>
    Task<string?> GetRawAsync(string gameId, StateOwnerKind ownerKind, string ownerId, string @namespace, string key);

    /// <summary>Set (upsert) a single state value.</summary>
    Task SetAsync(string gameId, StateOwnerKind ownerKind, string ownerId, string @namespace, string key, string value);

    /// <summary>Get all state values for a given owner and namespace.</summary>
    Task<List<StateValue>> QueryAsync(string gameId, StateOwnerKind? ownerKind, string? ownerId, string? @namespace, IEnumerable<string>? keys = null);

    /// <summary>Batch set multiple values in a single transaction.</summary>
    Task SetBatchAsync(IEnumerable<(string gameId, StateOwnerKind ownerKind, string ownerId, string @namespace, string key, string value)> items);

    /// <summary>Append a change log entry.</summary>
    Task LogChangeAsync(string gameId, StateOwnerKind ownerKind, string ownerId,
        string @namespace, string key, string? oldValue, string newValue,
        string changeType, string? reason, string? sourceType, string? sourceId);
}
