namespace openLuo.Server.Security;

/// <summary>
/// 键控滑动窗口限流：1 分钟窗口 + 10 秒突发窗口（原 <c>openLuo.Infrastructure.Security.RateLimiter</c> 迁入 Server 并改为纯值构造）。
/// </summary>
/// <remarks>
/// 只保存最近一分钟的调用时间戳；条目闲置超过一分钟会在下次访问或例行清理时移除，避免 key 无限增长。
/// </remarks>
public sealed class RateLimiter(int requestsPerMinute = 600, int burstLimit = 60)
{
    private const int MaxTrackedKeys = 4096;

    private readonly Dictionary<string, Queue<DateTime>> _calls = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly int _requestsPerMinute = Math.Max(1, requestsPerMinute);
    private readonly int _burstLimit = Math.Max(1, burstLimit);

    /// <summary>记一次调用；超出窗口即拒绝。<paramref name="key"/> 为空视为放行（无身份的调用不在此治理）。</summary>
    public bool Allow(string? key, int? maxPerMinute = null, int burst = -1)
    {
        if (string.IsNullOrWhiteSpace(key))
            return true;

        lock (_gate)
        {
            if (!_calls.TryGetValue(key, out var queue))
                _calls[key] = queue = new Queue<DateTime>();

            var now = DateTime.UtcNow;
            var oneMinuteAgo = now.AddMinutes(-1);
            while (queue.Count > 0 && queue.Peek() < oneMinuteAgo)
                queue.Dequeue();

            var effectiveBurst = burst > 0 ? burst : _burstLimit;
            if (queue.Count(t => t >= now.AddSeconds(-10)) >= effectiveBurst)
                return false;

            if (queue.Count >= (maxPerMinute ?? _requestsPerMinute))
                return false;

            queue.Enqueue(now);
            if (_calls.Count > MaxTrackedKeys)
                PruneExpired(oneMinuteAgo);
            return true;
        }
    }

    /// <summary>当前跟踪的 key 数（诊断用）。</summary>
    public int TrackedKeys
    {
        get
        {
            lock (_gate)
                return _calls.Count;
        }
    }

    /// <summary>清理已无有效时间戳的条目（调用方持锁）。</summary>
    private void PruneExpired(DateTime cutoff)
    {
        var stale = _calls.Where(pair => pair.Value.Count == 0 || pair.Value.Peek() < cutoff).Select(pair => pair.Key).ToList();
        foreach (var key in stale)
            _calls.Remove(key);
    }
}
