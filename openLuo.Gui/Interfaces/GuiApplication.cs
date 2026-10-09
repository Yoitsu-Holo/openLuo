using Avalonia;

namespace openLuo.Interfaces.GUI;

/// <summary>GUI 启动器：只持有 Hub 地址（内核在 Hub 侧，客户端不引用内核）。</summary>
public static class GuiApplication
{
    internal static string? HubUrl { get; private set; }

    public static void Launch(string hubUrl)
    {
        HubUrl = hubUrl;
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime([]);
    }
}
