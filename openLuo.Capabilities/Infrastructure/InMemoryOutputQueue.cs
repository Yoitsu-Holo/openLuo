using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Infrastructure;

/// <summary>
/// 内存输出队列（D6 + 解耦）：**每会话输出日志** + 唤醒信号。
///
/// <para>与旧实现的区别（解耦）：</para>
/// <list type="bullet">
///   <item>Enqueue 写入会话日志并发出唤醒信号，**永不阻塞内核**（信号通道满即丢弃，
///   消费者醒来后会重新扫描日志，故不丢内容）。旧实现用有界 <c>FullMode.Wait</c>，
///   在无消费者（或消费慢）时会阻塞回合。</item>
///   <item>每会话保留最近 <see cref="_maxRetainedPerSession"/> 条，供断线续传
///   （<see cref="ReadSince"/>）；序号全局单调（会话内亦单调）。</item>
///   <item>Ack/Fail 仅记录投递状态，不影响入队与消费游标。</item>
/// </list>
///
/// <para>并发：<c>Enqueue</c> 可被并行工具并发调用（每会话 <c>Gate</c> 保护日志与序号分配）；
/// <c>ReadAsync</c> 为单消费者语义（与平台适配层/Hub 独占消费一致）。</para>
/// </summary>
public sealed class InMemoryOutputQueue : IOutputQueue
{
    private sealed record Entry(long Sequence, OutputItem Item, DeliveryState State);

    private sealed class SessionLog
    {
        public readonly object Gate = new();
        public readonly LinkedList<Entry> Entries = new();

        /// <summary>已被 ReadAsync 取走的最大序号（防重复产出，与 Ack 无关）。</summary>
        public long ReadCursor;
    }

    private readonly ConcurrentDictionary<string, SessionLog> _logs = new(StringComparer.Ordinal);
    private readonly Channel<bool> _signal;
    private readonly int _maxRetainedPerSession;
    private long _nextSequence;

    public InMemoryOutputQueue(int maxRetainedPerSession = 256)
    {
        _maxRetainedPerSession = Math.Max(16, maxRetainedPerSession);

        // 仅用于唤醒消费者：容量 1 + DropWrite，满则丢弃唤醒，Enqueue 永不阻塞。
        _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    }

    public ValueTask<long> EnqueueAsync(OutputItem item, CancellationToken ct = default)
    {
        var key = KeyOf(item.ConversationId);
        var log = _logs.GetOrAdd(key, static _ => new SessionLog());

        long sequence;
        lock (log.Gate)
        {
            // 跨会话共享的序号必须以原子方式分配：不同会话持不同 Gate，
            // 普通 ++ 会在并行 Enqueue 时竞态（重复/跳号）。
            sequence = Interlocked.Increment(ref _nextSequence);
            log.Entries.AddLast(new Entry(sequence, item with { Sequence = sequence }, DeliveryState.Pending));

            // 滑窗淘汰最旧（这些项的续传能力随之失效，属预期）。
            while (log.Entries.Count > _maxRetainedPerSession)
                log.Entries.RemoveFirst();
        }

        _signal.Writer.TryWrite(true);
        return ValueTask.FromResult(sequence);
    }

    public async IAsyncEnumerable<OutputItem> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var item in Drain())
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
            }

            // 等待下一条唤醒；若期间已入队，信号已被消费（TryWrite 折叠）。
            try
            {
                await _signal.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                yield break;
            }
        }
    }

    /// <summary>取走所有会话中序号大于各自 ReadCursor 且未永久失败/取消的项（单消费者语义）。</summary>
    private List<OutputItem> Drain()
    {
        var result = new List<OutputItem>();
        foreach (var log in _logs.Values)
        {
            lock (log.Gate)
            {
                foreach (var entry in log.Entries)
                {
                    if (entry.Sequence <= log.ReadCursor)
                        continue;

                    log.ReadCursor = entry.Sequence;

                    if (entry.State is DeliveryState.PermanentFailure or DeliveryState.Cancelled)
                        continue;

                    result.Add(entry.Item);
                }
            }
        }

        result.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
        return result;
    }

    public Task AckAsync(long sequence, CancellationToken ct = default)
        => UpdateStateAsync(sequence, DeliveryState.Delivered);

    public Task FailAsync(long sequence, bool permanent, CancellationToken ct = default)
        => UpdateStateAsync(sequence, permanent ? DeliveryState.PermanentFailure : DeliveryState.RetryableFailure);

    private Task UpdateStateAsync(long sequence, DeliveryState state)
    {
        foreach (var log in _logs.Values)
        {
            lock (log.Gate)
            {
                for (var node = log.Entries.First; node is not null; node = node.Next)
                {
                    if (node.Value.Sequence != sequence)
                        continue;

                    node.Value = node.Value with { State = state };
                    return Task.CompletedTask;
                }
            }
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<OutputItem> ReadSince(string? conversationId, long sinceSequence)
    {
        var result = new List<OutputItem>();

        if (conversationId is null)
        {
            foreach (var log in _logs.Values)
                Collect(log, sinceSequence, result);
        }
        else if (_logs.TryGetValue(KeyOf(conversationId), out var log))
        {
            Collect(log, sinceSequence, result);
        }

        result.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
        return result;
    }

    private static void Collect(SessionLog log, long sinceSequence, List<OutputItem> sink)
    {
        lock (log.Gate)
        {
            foreach (var entry in log.Entries)
            {
                if (entry.Sequence <= sinceSequence)
                    continue;

                // 已确认投递或永久失败的不补发。
                if (entry.State is DeliveryState.Delivered or DeliveryState.PermanentFailure or DeliveryState.Cancelled)
                    continue;

                sink.Add(entry.Item);
            }
        }
    }

    private static string KeyOf(string? conversationId) => conversationId ?? string.Empty;
}
