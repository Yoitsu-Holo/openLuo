namespace openLuo.Modules.WorldState.Core.Models;

/// <summary>
/// Snapshot of all relevant state values for plugin consumption.
/// Replaces the old resourceSnapshot pattern.
/// </summary>
public class StateSnapshot
{
    /// <summary>Character status values (char_status namespace). Key = state key, Value = value string.</summary>
    public Dictionary<string, string> CharStatus { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>World state values (world_state namespace).</summary>
    public Dictionary<string, string> WorldState { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Game resource values (game_resource namespace).</summary>
    public Dictionary<string, string> GameResource { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scene state values (scene_state namespace).</summary>
    public Dictionary<string, string> SceneState { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Any additional namespace values not fitting the above categories.</summary>
    public Dictionary<string, Dictionary<string, string>> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
