using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Collections.Concurrent;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Infrastructure;

/// <summary>
/// 内存输出队列（D6）。按 Sequence 单调分配；消费方顺序读取，成功发送后 Ack。
/// 未 Ack 项保留；permanent 失败项被移除。进程重启丢失可接受。
/// </summary>
public sealed class InMemoryOutputQueue : IOutputQueue
{
    private sealed record Entry(long Sequence, OutputItem Item, DeliveryState State);

    private readonly Channel<Entry> _channel;
    private readonly ConcurrentDictionary<long, Entry> _pending = new();
    private long _nextSequence;
    private readonly object _gate = new();

    public InMemoryOutputQueue(int capacity = 1024)
    {
        _channel = Channel.CreateBounded<Entry>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public async ValueTask<long> EnqueueAsync(OutputItem item, CancellationToken ct = default)
    {
        long sequence;
        lock (_gate)
            sequence = ++_nextSequence;

        var output = new OutputItem
        {
            Id = item.Id,
            Kind = item.Kind,
            Payload = item.Payload,
            SourceCapability = item.SourceCapability,
            CreatedAtUtc = item.CreatedAtUtc,
            Fingerprint = item.Fingerprint,
            Sequence = sequence,
            ConversationId = item.ConversationId
        };
        var entry = new Entry(sequence, output, DeliveryState.Pending);
        _pending[sequence] = entry;
        await _channel.Writer.WriteAsync(entry, ct);
        return sequence;
    }

    public async IAsyncEnumerable<OutputItem> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var entry in _channel.Reader.ReadAllAsync(ct))
        {
            // 状态以 _pending 为准（FailAsync 只更新字典，channel 里是旧副本）
            if (!_pending.TryGetValue(entry.Sequence, out var current))
                continue;
            if (current.State == DeliveryState.PermanentFailure || current.State == DeliveryState.Cancelled)
                continue;
            yield return current.Item;
        }
    }

    public Task AckAsync(long sequence, CancellationToken ct = default)
    {
        _pending.TryRemove(sequence, out _);
        return Task.CompletedTask;
    }

    public Task FailAsync(long sequence, bool permanent, CancellationToken ct = default)
    {
        if (!_pending.TryGetValue(sequence, out var entry))
            return Task.CompletedTask;

        _pending[sequence] = permanent
            ? entry with { State = DeliveryState.PermanentFailure }
            : entry with { State = DeliveryState.RetryableFailure };
        return Task.CompletedTask;
    }
}
