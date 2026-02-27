using openLuo.Modules.Executor.Application.StateUpdate;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterStateUpdateResult
{
    public IReadOnlyList<StateDelta> Deltas { get; init; } = [];
    public string Reason { get; init; } = string.Empty;
    public double? Confidence { get; init; }
}
