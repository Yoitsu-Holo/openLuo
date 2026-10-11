namespace openLuo.Server;

/// <summary>Hub 运行指标（进程内计数）。</summary>
internal sealed class HubMetrics
{
    private long _turns;
    private long _errors;
    private int _clients;

    public long Turns => Interlocked.Read(ref _turns);
    public long Errors => Interlocked.Read(ref _errors);
    public int Clients => Volatile.Read(ref _clients);

    public void TurnStarted() => Interlocked.Increment(ref _turns);
    public void ErrorReported() => Interlocked.Increment(ref _errors);
    public void ClientOpened() => Interlocked.Increment(ref _clients);
    public void ClientClosed() => Interlocked.Decrement(ref _clients);
}
