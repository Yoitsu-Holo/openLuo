using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

/// <summary>Applies state mutations with validation, clamping, and logging.</summary>
public interface IStateMutationService
{
    /// <summary>Apply a batch of state mutations atomically.</summary>
    Task<List<StateMutationResult>> ApplyAsync(string gameId, IEnumerable<StateMutation> mutations);
}
