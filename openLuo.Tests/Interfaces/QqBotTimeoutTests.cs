using openLuo.Interfaces.QQbot;

namespace openLuo.Interfaces.Tests;

public sealed class QqBotTimeoutTests
{
    [Fact]
    public void NewConfig_UsesTwoMinuteMilkyRequestTimeout()
    {
        var config = new QqBotConfig();

        Assert.Equal(120, config.RequestTimeoutSeconds);
    }

    [Fact]
    public void Clone_PreservesCustomMilkyRequestTimeout()
    {
        var config = new QqBotConfig
        {
            RequestTimeoutSeconds = 180
        };

        var clone = config.Clone();

        Assert.Equal(180, clone.RequestTimeoutSeconds);
    }
}
