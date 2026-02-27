using openLuo.Core.Models;

namespace openLuo.Core.Interfaces;

/// <summary>
/// Repository for character data persistence.
/// </summary>
public interface ICharacterRepository
{
    /// <summary>
    /// Get character by archetype ID.
    /// </summary>
    /// <param name="archetypeId">Archetype identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Character if found, null otherwise.</returns>
    Task<Character?> GetByArchetypeIdAsync(string archetypeId, CancellationToken ct = default);

    /// <summary>
    /// Get a character by game and character ID.
    /// </summary>
    Task<Character?> GetByIdAsync(string gameId, string characterId, CancellationToken ct = default);

    /// <summary>
    /// List all enabled characters for the specified game.
    /// </summary>
    Task<IReadOnlyList<Character>> ListByGameIdAsync(string gameId, CancellationToken ct = default);

    /// <summary>
    /// Save or update character data.
    /// </summary>
    /// <param name="character">Character to save.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(Character character, CancellationToken ct = default);

    /// <summary>
    /// Record an affection change event for a character.
    /// </summary>
    /// <param name="evt">Affection event to record.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordAffectionEventAsync(AffectionEvent evt, CancellationToken ct = default);
}

/// <summary>
/// Repository for game state persistence.
/// </summary>
public interface IGameStateRepository
{
    /// <summary>
    /// Get current game state.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>GameState if exists, null otherwise.</returns>
    Task<GameState?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Get game state by explicit game identifier.
    /// </summary>
    Task<GameState?> GetAsync(string gameId, CancellationToken ct = default);

    /// <summary>
    /// List all game states ordered by most recently updated first.
    /// </summary>
    Task<IReadOnlyList<GameState>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Save or update game state.
    /// </summary>
    /// <param name="state">Game state to save.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(GameState state, CancellationToken ct = default);
}

/// <summary>
/// Repository for player inventory management.
/// </summary>
public interface IInventoryRepository
{
    /// <summary>
    /// Get all inventory items with quantities.
    /// </summary>
    /// <param name="gameId">Game/save identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary mapping item IDs to quantities.</returns>
    Task<Dictionary<string, int>> GetAllAsync(string gameId, CancellationToken ct = default);

    /// <summary>
    /// Add items to inventory.
    /// </summary>
    /// <param name="gameId">Game/save identifier.</param>
    /// <param name="itemId">Item identifier.</param>
    /// <param name="quantity">Quantity to add (default 1).</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddItemAsync(string gameId, string itemId, int quantity = 1, CancellationToken ct = default);

    /// <summary>
    /// Remove items from inventory.
    /// </summary>
    /// <param name="gameId">Game/save identifier.</param>
    /// <param name="itemId">Item identifier.</param>
    /// <param name="quantity">Quantity to remove (default 1).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if removal succeeded, false if insufficient quantity.</returns>
    Task<bool> RemoveItemAsync(string gameId, string itemId, int quantity = 1, CancellationToken ct = default);
}
