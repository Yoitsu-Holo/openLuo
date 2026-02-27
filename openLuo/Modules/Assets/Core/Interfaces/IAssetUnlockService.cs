using openLuo.Modules.Assets.Core.Models;

namespace openLuo.Modules.Assets.Core.Interfaces;

/// <summary>
/// Manages unlock records — tracks which entities have been unlocked for which owners.
/// Used for entitlement checks (e.g., "has character unlocked this scene CG?").
/// </summary>
public interface IAssetUnlockService
{
    /// <summary>
    /// Record an unlock event. Returns the new unlockId.
    /// </summary>
    /// <param name="gameId">Game session ID.</param>
    /// <param name="ownerKind">Kind of entity that received the unlock (e.g., "character").</param>
    /// <param name="ownerId">ID of the entity that received the unlock.</param>
    /// <param name="entityType">Type of entity being unlocked (e.g., "asset").</param>
    /// <param name="entityId">ID of the entity being unlocked.</param>
    /// <param name="unlockType">Mechanism of unlock (e.g., "affection_threshold", "story_event", "purchase").</param>
    /// <param name="metadataJson">Optional JSON metadata for unlock-specific context.</param>
    /// <returns>New unlockId.</returns>
    Task<string> UnlockAsync(
        string gameId,
        string ownerKind,
        string ownerId,
        string entityType,
        string entityId,
        string unlockType,
        string? metadataJson = null);

    /// <summary>
    /// Get unlock records with optional filters.
    /// </summary>
    /// <param name="gameId">Game session ID (required).</param>
    /// <param name="ownerKind">Filter by owner entity kind.</param>
    /// <param name="ownerId">Filter by owner entity ID.</param>
    /// <param name="entityType">Filter by unlocked entity type.</param>
    /// <param name="entityId">Filter by unlocked entity ID.</param>
    Task<List<UnlockRecord>> GetUnlocksAsync(
        string gameId,
        string? ownerKind = null,
        string? ownerId = null,
        string? entityType = null,
        string? entityId = null);
}
