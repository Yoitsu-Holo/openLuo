namespace openLuo.Core.Models;

/// <summary>
/// Represents a character in the game.
/// </summary>
public class Character
{
    /// <summary>Unique character identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning game session ID.</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>Character archetype identifier.</summary>
    public string ArchetypeId { get; set; } = string.Empty;

    /// <summary>Character display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Display priority in multi-character lists (lower means earlier).</summary>
    public int DisplayPriority { get; set; } = 100;

    /// <summary>Whether this character is currently enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>JSON profile describing role/personality parameters.</summary>
    public string RoleProfileJson { get; set; } = "{}";

    /// <summary>JSON policy for runtime controls like timeout and budget.</summary>
    public string AgentPolicyJson { get; set; } = "{}";

    /// <summary>UTC timestamp when character was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when character record was last updated.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Relationship progression stages between player and character.
/// </summary>
public enum RelationshipStage
{
    /// <summary>No prior interaction.</summary>
    Stranger,

    /// <summary>Initial acquaintance.</summary>
    Acquaintance,

    /// <summary>Friendly relationship.</summary>
    Friend,

    /// <summary>Close friendship.</summary>
    CloseFriend,

    /// <summary>Romantic relationship.</summary>
    Lover
}
