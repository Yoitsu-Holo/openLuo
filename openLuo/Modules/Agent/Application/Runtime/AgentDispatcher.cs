namespace openLuo.Modules.Agent.Application;

public interface IAgentDispatcher
{
    Task RegisterAsync(string characterId, IAgentMailbox mailbox, CancellationToken ct = default);

    Task UnregisterAsync(string characterId, CancellationToken ct = default);

    Task DispatchAsync(AgentMessage message, CancellationToken ct = default);

    Task<AgentMessage?> RequestAsync(AgentMessage message, TimeSpan timeout, CancellationToken ct = default);
}

public sealed class AgentDispatcher : IAgentDispatcher
{
    private readonly Dictionary<string, IAgentMailbox> _mailboxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public Task RegisterAsync(string characterId, IAgentMailbox mailbox, CancellationToken ct = default)
    {
        lock (_gate)
            _mailboxes[characterId] = mailbox;
        return Task.CompletedTask;
    }

    public Task UnregisterAsync(string characterId, CancellationToken ct = default)
    {
        lock (_gate)
            _mailboxes.Remove(characterId);
        return Task.CompletedTask;
    }

    public async Task DispatchAsync(AgentMessage message, CancellationToken ct = default)
    {
        var mailbox = TryResolveMailbox(message.To);
        if (mailbox is null)
            return;

        await mailbox.EnqueueAsync(new AgentDispatchItem
        {
            Message = message,
            HandlingToken = ct
        }, ct);
    }

    public async Task<AgentMessage?> RequestAsync(AgentMessage message, TimeSpan timeout, CancellationToken ct = default)
    {
        var mailbox = TryResolveMailbox(message.To);
        if (mailbox is null)
            return null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tcs = new TaskCompletionSource<AgentMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await mailbox.EnqueueAsync(new AgentDispatchItem
        {
            Message = message,
            ReplySink = tcs,
            HandlingToken = timeoutCts.Token
        }, timeoutCts.Token);

        try
        {
            if (message.ExecutionContext is null)
            {
                timeoutCts.CancelAfter(timeout);
                return await tcs.Task.WaitAsync(timeoutCts.Token);
            }

            while (true)
            {
                if (tcs.Task.IsCompleted)
                    return await tcs.Task;

                if (message.ExecutionContext.IsExpired(out _))
                {
                    timeoutCts.Cancel();
                    return null;
                }

                using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var wait = Task.Delay(TimeSpan.FromMilliseconds(250), pollCts.Token);
                var completed = await Task.WhenAny(tcs.Task, wait);
                if (completed == tcs.Task)
                {
                    pollCts.Cancel();
                    return await tcs.Task;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private IAgentMailbox? TryResolveMailbox(string characterId)
    {
        lock (_gate)
            return _mailboxes.GetValueOrDefault(characterId);
    }
}
