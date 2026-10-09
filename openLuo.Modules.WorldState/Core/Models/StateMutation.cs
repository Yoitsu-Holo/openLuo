namespace openLuo.Modules.WorldState.Core.Models;

/// <summary>A single state change to apply.</summary>
public class StateMutation
{
    public string Namespace { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public StateOwnerKind OwnerKind { get; set; } = StateOwnerKind.Game;
    public string OwnerId { get; set; } = string.Empty;
    /// <summary>"set" or "delta".</summary>
    public string Op { get; set; } = "set";
    public string Value { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
}

/// <summary>Result of applying a single mutation.</summary>
public class StateMutationResult
{
    public string Namespace { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public StateOwnerKind OwnerKind { get; set; } = StateOwnerKind.Game;
    public string OwnerId { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string NewValue { get; set; } = string.Empty;
    public bool Clamped { get; set; } = false;
    public bool Ok { get; set; } = true;
    public string? Error { get; set; }
}
