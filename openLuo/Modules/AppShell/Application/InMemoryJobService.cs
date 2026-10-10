using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using openLuo.Capabilities.Core;

namespace openLuo.Modules.AppShell.Application;

/// <summary>
/// <see cref="IJobService"/> 的内存实现：按 kind 注册 <see cref="IJobHandler"/>，
/// 异步执行并发布作业事件（受理 / 进度 / 完成 / 失败）。进程重启丢失（长任务可接受）。
/// </summary>
public sealed class InMemoryJobService : IJobService
{
    private readonly Dictionary<string, IJobHandler> _handlers;
    private readonly Infrastructure.Persistence.HubStore? _store;
    private readonly ConcurrentDictionary<string, JobInfo> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);
    private readonly Channel<JobEvent> _events = Channel.CreateUnbounded<JobEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    public InMemoryJobService(IEnumerable<IJobHandler>? handlers = null, Infrastructure.Persistence.HubStore? store = null)
    {
        _handlers = (handlers ?? []).ToDictionary(h => h.Kind, StringComparer.OrdinalIgnoreCase);
        _store = store;

        if (store is null)
            return;

        // 重启恢复：进行中的作业标记失败（处理器不可续跑），再载入历史作业
        store.MarkRunningJobsFailed("interrupted by hub restart");
        foreach (var job in store.LoadJobs())
            _jobs[job.Id] = job;
    }

    public Task<JobInfo> SubmitAsync(string kind, JsonNode? payload, string? sessionId = null, CancellationToken ct = default)
    {
        if (!_handlers.TryGetValue(kind, out var handler))
            throw new InvalidOperationException($"no handler registered for job kind: {kind}");

        var info = new JobInfo(
            Id: "job_" + Guid.NewGuid().ToString("N"),
            Kind: kind,
            Status: JobStatusNames.Queued,
            Progress: 0,
            Message: null,
            SessionId: sessionId,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null);

        _jobs[info.Id] = info;
        _store?.UpsertJob(info);
        _events.Writer.TryWrite(new JobEvent("job.accepted", info));
        _ = RunAsync(handler, info, payload);
        return Task.FromResult(info);
    }

    private async Task RunAsync(IJobHandler handler, JobInfo info, JsonNode? payload)
    {
        var cts = new CancellationTokenSource();
        _running[info.Id] = cts;

        Update(info.Id, info with { Status = JobStatusNames.Running }, "job.progress");

        try
        {
            await handler.RunAsync(new JobContext
            {
                Id = info.Id,
                Kind = info.Kind,
                Payload = payload,
                SessionId = info.SessionId,
                Progress = (progress, message) =>
                {
                    if (_jobs.TryGetValue(info.Id, out var current))
                        Update(info.Id, current with { Progress = progress, Message = message }, "job.progress");
                },
            }, cts.Token);

            if (_jobs.TryGetValue(info.Id, out var done))
                Update(info.Id, done with { Status = JobStatusNames.Succeeded, Progress = 1, CompletedAt = DateTimeOffset.UtcNow }, "job.completed");
        }
        catch (OperationCanceledException)
        {
            if (_jobs.TryGetValue(info.Id, out var cancelled))
                Update(info.Id, cancelled with { Status = JobStatusNames.Cancelled, CompletedAt = DateTimeOffset.UtcNow }, "job.failed", "cancelled");
        }
        catch (Exception ex)
        {
            if (_jobs.TryGetValue(info.Id, out var failed))
                Update(info.Id, failed with { Status = JobStatusNames.Failed, Message = ex.Message, CompletedAt = DateTimeOffset.UtcNow }, "job.failed", ex.Message);
        }
        finally
        {
            _running.TryRemove(info.Id, out _);
            cts.Dispose();
        }
    }

    private void Update(string id, JobInfo info, string eventKind, string? error = null)
    {
        _jobs[id] = info;
        _store?.UpsertJob(info);
        _events.Writer.TryWrite(new JobEvent(eventKind, info, error));
    }

    public JobInfo? Get(string id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public IReadOnlyList<JobInfo> List() => _jobs.Values.OrderBy(j => j.CreatedAt).ToList();

    public bool Cancel(string id)
    {
        if (!_running.TryGetValue(id, out var cts))
            return false;

        cts.Cancel();
        return true;
    }

    public async IAsyncEnumerable<JobEvent> WatchAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in _events.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return evt;
    }
}
