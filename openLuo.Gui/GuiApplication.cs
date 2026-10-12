using Avalonia;

namespace openLuo.Gui;

/// <summary>GUI 启动器：只持有 Hub 地址与客户端身份（内核在 Hub 侧，客户端不引用内核）。</summary>
public static class GuiApplication
{
    internal static string? HubUrl { get; private set; }
    internal static string SubjectId { get; private set; } = "builtin-rin";
    internal static string AgentId { get; private set; } = "companion";

    /// <summary>会话寻址：与其它端填同一个值即共享同一段对话（见协议 §6.4）。</summary>
    internal static string? ConversationId { get; private set; }

    public static void Launch(string hubUrl, string subjectId = "builtin-rin", string agentId = "companion", string? conversationId = null)
    {
        HubUrl = hubUrl;
        SubjectId = subjectId;
        AgentId = agentId;
        ConversationId = conversationId;
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime([]);
    }
}
