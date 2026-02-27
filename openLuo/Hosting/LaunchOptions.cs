using Microsoft.Extensions.Logging;

namespace openLuo.Hosting;

public sealed class LaunchOptions
{
    public required LaunchMode Mode { get; init; }

    public static LaunchOptions? Parse(string[] args)
    {
        var logger = BootstrapLogger.Create<LaunchOptions>();
        var useCli = args.Contains("--cli", StringComparer.OrdinalIgnoreCase);
        var useTui = args.Contains("--tui", StringComparer.OrdinalIgnoreCase);
        var useQq = args.Contains("--qq", StringComparer.OrdinalIgnoreCase);

        var selectedCount = (useCli ? 1 : 0) + (useTui ? 1 : 0) + (useQq ? 1 : 0);
        if (selectedCount > 1)
        {
            logger.LogError("--cli、--tui 与 --qq 不能同时使用。");
            return null;
        }

        return new LaunchOptions
        {
            Mode = useQq
                ? LaunchMode.QqBot
                : useTui
                    ? LaunchMode.Tui
                    : LaunchMode.Cli
        };
    }
}
