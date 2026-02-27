using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class NullTurnMessageEmitter : ITurnMessageEmitter
{
    public static NullTurnMessageEmitter Instance { get; } = new();

    public bool HasPublishedPublicMessage => false;

    public Task PublishAsync(TurnMessage message, CancellationToken ct = default) => Task.CompletedTask;
}
