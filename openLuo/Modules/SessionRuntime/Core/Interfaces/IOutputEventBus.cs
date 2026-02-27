using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

public interface IOutputEventBus
{
    Task PublishAsync(GameEvent @event, CancellationToken ct = default);

    IAsyncEnumerable<GameEvent> StreamAsync(string sessionId, CancellationToken ct = default);

    IReadOnlyList<GameEvent> Drain(string sessionId);

    void Complete(string sessionId);
}
