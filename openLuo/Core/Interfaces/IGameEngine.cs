using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Core.Interfaces;

/// <summary>
/// Core game engine orchestrating command execution, state management, and game flow.
/// </summary>
public interface IGameEngine
{
    /// <summary>
    /// Execute a raw user input command.
    /// </summary>
    /// <param name="gameId">Target game identifier.</param>
    /// <param name="rawInput">User input string (e.g., "/chat hello", "/give item").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Command execution result with output or error.</returns>
    Task<CommandResult> ExecuteAsync(string gameId, string rawInput, CancellationToken ct = default);

    /// <summary>
    /// Get current game state snapshot.
    /// </summary>
    /// <param name="gameId">Target game identifier.</param>
    /// <returns>Current GameState including day, time, location, etc.</returns>
    Task<GameState> GetStateAsync(string gameId, CancellationToken ct = default);

    /// <summary>
    /// Initialize a new game session.
    /// </summary>
    /// <param name="gameId">Target game identifier.</param>
    /// <param name="archetypeId">Character archetype/scenario identifier.</param>
    /// <param name="playerName">Player character name.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> InitializeAsync(string gameId, string archetypeId, string playerName, CancellationToken ct = default);
}
