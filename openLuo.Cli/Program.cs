using openLuo.Cli;
using openLuo.Client;
using openLuo.Protocol;

// openLuo CLI 客户端（独立进程）：只依赖 openLuo.Protocol + openLuo.Client，连到 Hub 的数据面。
// Hub 地址：OPENLUO_HUB_URL；默认 ws://127.0.0.1:<DefaultPort>/v1/stream
var hubUrl = Environment.GetEnvironmentVariable("OPENLUO_HUB_URL")
    ?? $"ws://127.0.0.1:{ProtocolInfo.DefaultPort}{ProtocolInfo.StreamPath}";

var subjectId = Environment.GetEnvironmentVariable("OPENLUO_SUBJECT_ID") ?? "builtin-rin";
var agentId = Environment.GetEnvironmentVariable("OPENLUO_AGENT_ID") ?? "companion";

// 会话寻址：与其它端（例如手机 QQ 桥配置的 privateConversationId）填同一个值，即共享同一段对话上下文；
// 不填则每次启动新建一段对话（原行为）。
var conversationId = Environment.GetEnvironmentVariable("OPENLUO_CONVERSATION_ID");

// 说话人稳定身份与会话内线程（都可选）：UserId 用于区分"谁在说话"（多人共用一台机器/多账号时
// Hub 才能把回合归因到正确的人并分区记忆），ThreadId 用于同一会话里的并行话题。
// 不填 = 不上送 = 行为与从前一致（归因回退到会话主体）。
var userId = Environment.GetEnvironmentVariable("OPENLUO_USER_ID");
var threadId = Environment.GetEnvironmentVariable("OPENLUO_THREAD_ID");

try
{
    await new CliHubApp(hubUrl, subjectId, agentId, conversationId: conversationId,
        userId: userId, threadId: threadId).RunAsync(Console.In);
    return 0;
}
catch (HubConnectException ex)
{
    // 未起 Hub 是最常见的用法错误：给一行可读提示，而不是抛栈
    Console.Error.WriteLine(ex.Message);
    return 1;
}
