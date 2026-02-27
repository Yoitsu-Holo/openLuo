namespace openLuo.Modules.Gameplay.Core.Interfaces;

/// <summary>
/// Coordinates state evaluation across plugins and LLM.
/// </summary>
public interface IStateEvaluationCoordinator
{
    /// <summary>
    /// Evaluates and applies state changes based on interaction context.
    /// </summary>
    Task<StateEvaluationResult> EvaluateStatesAsync(
        string gameId,
        string characterId,
        string archetypeId,
        string beatSummary,
        string[] moodSignals,
        string playerMessage,
        string interactionType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of state evaluation.
/// </summary>
public record StateEvaluationResult(
    StateChange[] StateChanges,
    string Reason)
{
    public StateAppliedChange[] AppliedChanges { get; init; } = [];
}

/// <summary>
/// Single state change entry.
/// </summary>
public record StateChange(
    string ResourceId,
    string Op,
    string Value);

public record StateAppliedChange(
    string ResourceId,
    string Namespace,
    string Op,
    string? OldValue,
    string NewValue);
