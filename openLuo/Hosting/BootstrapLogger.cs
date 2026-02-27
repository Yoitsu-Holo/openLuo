using Microsoft.Extensions.Logging;

namespace openLuo.Hosting;

internal static class BootstrapLogger
{
    private static readonly ILoggerFactory Factory = LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
        });
        builder.SetMinimumLevel(LogLevel.Information);
    });

    public static ILogger<T> Create<T>() => Factory.CreateLogger<T>();

    public static ILogger Create(string category) => Factory.CreateLogger(category);
}
