using openLuo.Server.Security;
using Xunit;

namespace openLuo.Tests.Security;

/// <summary>
/// 键控滑动窗口限流（现位于 <c>openLuo.Server.Security</c>，服务于 Hub 入口治理，见 hub-protocol §4.9）。
/// </summary>
public class RateLimiterTests
{
    [Fact]
    public void Allow_AllowsUnderLimit()
    {
        var limiter = new RateLimiter(requestsPerMinute: 10, burstLimit: 20);

        for (int i = 0; i < 10; i++)
            Assert.True(limiter.Allow("client-a"));
    }

    [Fact]
    public void Allow_BlocksOverLimit()
    {
        var limiter = new RateLimiter(requestsPerMinute: 10, burstLimit: 20);

        for (int i = 0; i < 10; i++)
            limiter.Allow("client-a");

        Assert.False(limiter.Allow("client-a"));
    }

    [Fact]
    public void Allow_IsolatesKeys()
    {
        var limiter = new RateLimiter(requestsPerMinute: 10, burstLimit: 20);

        for (int i = 0; i < 10; i++)
            limiter.Allow("client-a");

        Assert.True(limiter.Allow("client-b"));
    }

    [Fact]
    public void Allow_RespectsPerCallOverride()
    {
        var limiter = new RateLimiter();

        for (int i = 0; i < 5; i++)
            Assert.True(limiter.Allow("client-a", maxPerMinute: 5, burst: 10));

        Assert.False(limiter.Allow("client-a", maxPerMinute: 5, burst: 10));
    }

    [Fact]
    public void Allow_BurstWindowBlocksBurst()
    {
        // 分钟配额很宽，但 10 秒突发窗口只有 2 → 第 3 次立刻被挡（证明突发与分钟是两道独立闸）
        var limiter = new RateLimiter(requestsPerMinute: 100, burstLimit: 2);

        Assert.True(limiter.Allow("client-a"));
        Assert.True(limiter.Allow("client-a"));
        Assert.False(limiter.Allow("client-a"));
    }

    [Fact]
    public void Allow_EmptyKeyAlwaysAllowed()
    {
        var limiter = new RateLimiter(requestsPerMinute: 1, burstLimit: 1);

        Assert.True(limiter.Allow(null));
        Assert.True(limiter.Allow(""));
        Assert.True(limiter.Allow("   "));
    }
}
