using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using openLuo.Capabilities.Core;

namespace openLuo.Modules.AppShell.Application;

/// <summary>
/// <see cref="ISchedulerService"/> 的内存实现：支持一次性（<c>At</c>）触发；
/// 周期（<c>Cron</c>）暂不支持（<see cref="Add"/> 抛 <see cref="InvalidOperationException"/>，Hub 回 1202）。
/// 进程重启丢失（后续可持久化到 SQLite）。
/// </summary>
public sealed class InMemorySchedulerService : ISchedulerService, IAsyncDisposable
{
    private sealed class Entry
    {
        public required ScheduleInfo Info { get; set; }
        public DateTimeOffset? Due { get; set; }
    }

    private static readonly TimeSpan CatchUpGrace = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Infrastructure.Persistence.HubStore? _store;
    private readonly Channel<ScheduleDue> _events = Channel.CreateUnbounded<ScheduleDue>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false,
    });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;

    public InMemorySchedulerService(Infrastructure.Persistence.HubStore? store = null)
    {
        _store = store;
        Restore(store);

        _pump = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
                FireDue();
        });
    }

    /// <summary>重启恢复：一次性调度若已过期——未超宽限期则立即触发，超期则跳过（禁用）。</summary>
    private void Restore(Infrastructure.Persistence.HubStore? store)
    {
        if (store is null)
            return;

        var now = DateTimeOffset.UtcNow;
        foreach (var info in store.LoadSchedules())
        {
            var effective = info;
            DateTimeOffset? due = null;

            if (info.Enabled && info.At is { } at)
            {
                if (at > now)
                {
                    due = at;
                }
                else if (now - at <= CatchUpGrace)
                {
                    due = now;   // 宽限期内：启动后立即补发一次
                }
                else
                {
                    effective = info with { Enabled = false, NextRunAt = null };
                    store.UpsertSchedule(effective);
                }
            }

            _entries[info.Id] = new Entry { Info = effective, Due = due };
        }
    }

    private void FireDue()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _entries.Values)
        {
            if (!entry.Info.Enabled || entry.Due is null || entry.Due > now)
                continue;

            // 一次性：触发后置为已禁用（周期调度后续再支持 Cron）
            entry.Due = null;
            entry.Info = entry.Info with { Enabled = false, NextRunAt = null };
            _store?.UpsertSchedule(entry.Info);
            _events.Writer.TryWrite(new ScheduleDue(entry.Info.Id, entry.Info.SessionId, entry.Info.Kind, entry.Info.Payload));
        }
    }

    public ScheduleInfo Add(string sessionId, string kind, DateTimeOffset? at, string? cron, JsonNode? payload)
    {
        if (!string.IsNullOrWhiteSpace(cron))
            throw new InvalidOperationException("cron schedules are not supported yet (use 'at')");

        if (at is null)
            throw new InvalidOperationException("schedule requires 'at' (one-shot)");

        var info = new ScheduleInfo(
            Id: "sch_" + Guid.NewGuid().ToString("N"),
            SessionId: sessionId,
            Kind: kind,
            At: at,
            Cron: null,
            Enabled: true,
            NextRunAt: at,
            Payload: payload);

        _entries[info.Id] = new Entry { Info = info, Due = at };
        _store?.UpsertSchedule(info);
        return info;
    }

    public ScheduleInfo? Get(string id) => _entries.TryGetValue(id, out var entry) ? entry.Info : null;

    public IReadOnlyList<ScheduleInfo> List() => _entries.Values.Select(e => e.Info).OrderBy(i => i.NextRunAt ?? DateTimeOffset.MaxValue).ToList();

    public bool Remove(string id)
    {
        var removed = _entries.TryRemove(id, out _);
        _store?.DeleteSchedule(id);
        return removed;
    }

    public async IAsyncEnumerable<ScheduleDue> WatchAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var due in _events.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return due;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try { await _pump.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
