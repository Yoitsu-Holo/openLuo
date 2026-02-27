namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionSubmitResult
{
    public required IReadOnlyList<GameEvent> Events { get; init; }
}
