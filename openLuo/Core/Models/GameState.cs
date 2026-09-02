namespace openLuo.Core.Models;

/// <summary>
/// Represents the current state of a game session.
/// </summary>
public class GameState
{
    /// <summary>Unique game session identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Player character name.</summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>Active story / character archetype identifier.</summary>
    public string ArchetypeId { get; set; } = string.Empty;

    /// <summary>Current active character ID for chat/task interactions.</summary>
    public string ActiveCharacterId { get; set; } = string.Empty;

    /// <summary>Current location in the game world.</summary>
    public string CurrentLocation { get; set; } = string.Empty;

    /// <summary>Current in-game day (1-indexed).</summary>
    public int CurrentDay { get; set; } = 1;

    /// <summary>Last in-game day when player interacted with a character (/chat, /give, etc.).</summary>
    public int LastInteractionDay { get; set; } = 1;

    /// <summary>Current in-game time in minutes (0-1439). 480 = 08:00, 1200 = 20:00.</summary>
    public int CurrentMinute { get; set; } = 480;

    /// <summary>UTC timestamp when game session was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of last state update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
