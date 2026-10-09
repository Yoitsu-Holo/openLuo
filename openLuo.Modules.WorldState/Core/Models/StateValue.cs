namespace openLuo.Modules.WorldState.Core.Models;

/// <summary>A runtime state value for a specific owner.</summary>
public class StateValue
{
    public string GameId { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public StateOwnerKind OwnerKind { get; set; } = StateOwnerKind.Game;
    public string OwnerId { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? ValueType { get; set; }
    /// <summary>True if value is the definition default (not persisted).</summary>
    public bool Defaulted { get; set; } = false;
    public string? UpdatedAt { get; set; }
}
