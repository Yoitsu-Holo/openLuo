using openLuo.Modules.AppShell.Application;

namespace openLuo.Infrastructure.Security;

public class RateLimiter(IRuntimeConfigCenter? configCenter = null)
{
    private readonly Dictionary<string, Queue<DateTime>> _callHistory = new();
    private readonly object _lock = new();
    private readonly IRuntimeConfigCenter? _configCenter = configCenter;

    public bool AllowCall(string pluginId, int? maxPerMinute = null, int burstLimit = 5)
    {
        lock (_lock)
        {
            if (!_callHistory.ContainsKey(pluginId))
            {
                _callHistory[pluginId] = new Queue<DateTime>();
            }

            var queue = _callHistory[pluginId];
            var now = DateTime.UtcNow;
            var oneMinuteAgo = now.AddMinutes(-1);
            var tenSecondsAgo = now.AddSeconds(-10);

            while (queue.Count > 0 && queue.Peek() < oneMinuteAgo)
            {
                queue.Dequeue();
            }

            var effectiveBurstLimit = burstLimit > 0 ? burstLimit : Math.Max(1, _configCenter?.GetSnapshot().Security.BurstLimit ?? 5);
            var recentBurst = queue.Count(t => t >= tenSecondsAgo);
            if (recentBurst >= effectiveBurstLimit)
            {
                return false;
            }

            var limit = maxPerMinute ?? Math.Max(1, _configCenter?.GetSnapshot().Security.RateLimitPerMinute ?? 10);
            if (queue.Count >= limit)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
