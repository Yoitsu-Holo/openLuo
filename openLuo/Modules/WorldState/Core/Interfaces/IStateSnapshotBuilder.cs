using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

/// <summary>Builds StateSnapshot objects for plugin consumption.</summary>
public interface IStateSnapshotBuilder
{
    /// <summary>
    /// Build a full state snapshot for the given game, optionally scoped to a character.
    /// </summary>
    Task<StateSnapshot> BuildAsync(string gameId, string? characterId = null);
}
