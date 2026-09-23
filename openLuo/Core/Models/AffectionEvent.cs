namespace openLuo.Core.Models;

/// <summary>
/// Records an affection change event for a character.
/// </summary>
public class AffectionEvent
{
    /// <summary>Unique event identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Character affected by this event.</summary>
    public string CharacterId { get; set; } = string.Empty;

    /// <summary>Reason for affection change (e.g., "gave gift", "had conversation").</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Affection point delta (positive or negative).</summary>
    public int Delta { get; set; } = 0;

    /// <summary>UTC timestamp when event occurred.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
