namespace openLuo.Modules.Agent.Application;

public sealed class AgentExecutionContext
{
    private readonly object _gate = new();
    private DateTimeOffset _lastProgressAtUtc;
    private string _lastProgressReason = "created";

    public AgentExecutionContext(
        string conversationId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset overallDeadlineUtc,
        TimeSpan stepIdleTimeout)
    {
        ConversationId = conversationId;
        StartedAtUtc = startedAtUtc;
        OverallDeadlineUtc = overallDeadlineUtc;
        StepIdleTimeout = stepIdleTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : stepIdleTimeout;
        _lastProgressAtUtc = startedAtUtc;
    }

    public string ConversationId { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset OverallDeadlineUtc { get; }
    public TimeSpan StepIdleTimeout { get; }

    public DateTimeOffset LastProgressAtUtc
    {
        get
        {
            lock (_gate)
                return _lastProgressAtUtc;
        }
    }

    public string LastProgressReason
    {
        get
        {
            lock (_gate)
                return _lastProgressReason;
        }
    }

    public void ReportProgress(string reason)
    {
        lock (_gate)
        {
            _lastProgressAtUtc = DateTimeOffset.UtcNow;
            _lastProgressReason = string.IsNullOrWhiteSpace(reason) ? "progress" : reason.Trim();
        }
    }

    public bool IsExpired(out string reason)
    {
        var now = DateTimeOffset.UtcNow;
        if (now >= OverallDeadlineUtc)
        {
            reason = "overall_timeout";
            return true;
        }

        var idleDeadline = LastProgressAtUtc + StepIdleTimeout;
        if (now >= idleDeadline)
        {
            reason = "idle_timeout";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public TimeSpan RemainingOverallTime
    {
        get
        {
            var remaining = OverallDeadlineUtc - DateTimeOffset.UtcNow;
            return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }
}
