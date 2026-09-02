namespace openLuo.Core.Interfaces;

/// <summary>
/// Aggregates status display from resource registry and plugin hooks.
/// </summary>
public interface IStatusAggregator
{
    /// <summary>
    /// Get aggregated status for a character.
    /// </summary>
    /// <param name="gameId">Game session identifier.</param>
    /// <param name="characterId">Character identifier.</param>
    /// <param name="archetypeId">Character archetype identifier.</param>
    /// <returns>Structured status data.</returns>
    Task<StatusData> GetStatusAsync(string gameId, string characterId, string archetypeId);
}

/// <summary>
/// Aggregated status data for display.
/// </summary>
public class StatusData
{
    public List<StatusItem> Items { get; set; } = [];
    public string AdditionalText { get; set; } = string.Empty;
}

/// <summary>
/// Individual status display item.
/// </summary>
public class StatusItem
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public string Value { get; set; } = string.Empty;
    public string? Max { get; set; }
    public string Group { get; set; } = "stat";
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
}
