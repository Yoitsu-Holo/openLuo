using openLuo.Infrastructure.Security;
using Xunit;

namespace openLuo.Tests.Security;

public class RateLimiterTests
{
    [Fact]
    public void AllowCall_AllowsUnderLimit()
    {
        var limiter = new RateLimiter();

        for (int i = 0; i < 10; i++)
        {
            Assert.True(limiter.AllowCall("plugin1", 10, burstLimit: 20));
        }
    }

    [Fact]
    public void AllowCall_BlocksOverLimit()
    {
        var limiter = new RateLimiter();

        for (int i = 0; i < 10; i++)
        {
            limiter.AllowCall("plugin1", 10, burstLimit: 20);
        }

        Assert.False(limiter.AllowCall("plugin1", 10, burstLimit: 20));
    }

    [Fact]
    public void AllowCall_IsolatesPlugins()
    {
        var limiter = new RateLimiter();

        for (int i = 0; i < 10; i++)
        {
            limiter.AllowCall("plugin1", 10, burstLimit: 20);
        }

        Assert.True(limiter.AllowCall("plugin2", 10, burstLimit: 20));
    }

    [Fact]
    public void AllowCall_RespectsCustomLimit()
    {
        var limiter = new RateLimiter();

        for (int i = 0; i < 5; i++)
        {
            Assert.True(limiter.AllowCall("plugin1", 5, burstLimit: 10));
        }

        Assert.False(limiter.AllowCall("plugin1", 5, burstLimit: 10));
    }
}
