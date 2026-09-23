using Avalonia;
using openLuo.Capabilities.Core;

namespace openLuo.Interfaces.GUI;

public static class GuiApplication
{
    internal static IAgentRuntime? Runtime { get; private set; }
    public static void Launch(IAgentRuntime runtime)
    {
        Runtime = runtime;
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime([]);
    }
}
