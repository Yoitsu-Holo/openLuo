using System.Collections.Concurrent;
using System.Threading.Channels;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class InMemoryOutputEventBus : IOutputEventBus
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<GameEvent>> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Channel<GameEvent>> _channels = new(StringComparer.OrdinalIgnoreCase);

    public async Task PublishAsync(GameEvent @event, CancellationToken ct = default)
    {
        var queue = _queues.GetOrAdd(@event.SessionId, _ => new ConcurrentQueue<GameEvent>());
        queue.Enqueue(@event);
        var channel = _channels.GetOrAdd(@event.SessionId, _ => Channel.CreateUnbounded<GameEvent>());
        await channel.Writer.WriteAsync(@event, ct);
    }

    public async IAsyncEnumerable<GameEvent> StreamAsync(
        string sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var queue = _queues.GetOrAdd(sessionId, _ => new ConcurrentQueue<GameEvent>());
        foreach (var @event in queue.ToArray())
            yield return @event;

        var channel = _channels.GetOrAdd(sessionId, _ => Channel.CreateUnbounded<GameEvent>());
        await foreach (var @event in channel.Reader.ReadAllAsync(ct))
            yield return @event;
    }

    public IReadOnlyList<GameEvent> Drain(string sessionId)
    {
        if (!_queues.TryGetValue(sessionId, out var queue))
            return [];

        var events = new List<GameEvent>();
        while (queue.TryDequeue(out var @event))
            events.Add(@event);
        return events;
    }

    public void Complete(string sessionId)
    {
        if (_channels.TryRemove(sessionId, out var channel))
            channel.Writer.TryComplete();
    }
}
