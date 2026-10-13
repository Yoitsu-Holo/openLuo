using openLuo.Client;
using openLuo.Gui;
using openLuo.Protocol;

// openLuo GUI 客户端（独立进程）：只依赖 openLuo.Protocol + openLuo.Client + Avalonia。
// Hub 地址：OPENLUO_HUB_URL；默认 ws://127.0.0.1:<DefaultPort>/v1/stream
var hubUrl = Environment.GetEnvironmentVariable("OPENLUO_HUB_URL")
    ?? $"ws://127.0.0.1:{ProtocolInfo.DefaultPort}{ProtocolInfo.StreamPath}";

var subjectId = Environment.GetEnvironmentVariable("OPENLUO_SUBJECT_ID") ?? "builtin-rin";
var agentId = Environment.GetEnvironmentVariable("OPENLUO_AGENT_ID") ?? "companion";
// 会话寻址：与其它端（CLI / QQ 桥的 privateConversationId）填同一个值即共享同一段对话
var conversationId = Environment.GetEnvironmentVariable("OPENLUO_CONVERSATION_ID");
// 说话人稳定身份与会话内线程（都可选）：不填 = 不上送 = 行为与从前一致
var userId = Environment.GetEnvironmentVariable("OPENLUO_USER_ID");
var threadId = Environment.GetEnvironmentVariable("OPENLUO_THREAD_ID");

try
{
    GuiApplication.Launch(hubUrl, subjectId, agentId, conversationId, userId, threadId);
    return 0;
}
catch (HubConnectException ex)
{
    // 未起 Hub 是最常见的用法错误：给一行可读提示，而不是抛栈
    Console.Error.WriteLine(ex.Message);
    return 1;
}
