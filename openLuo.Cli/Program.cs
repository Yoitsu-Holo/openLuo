using openLuo.Cli;
using openLuo.Client;
using openLuo.Protocol;

// openLuo CLI 客户端（独立进程）：只依赖 openLuo.Protocol + openLuo.Client，连到 Hub 的数据面。
// Hub 地址：OPENLUO_HUB_URL；默认 ws://127.0.0.1:<DefaultPort>/v1/stream
var hubUrl = Environment.GetEnvironmentVariable("OPENLUO_HUB_URL")
    ?? $"ws://127.0.0.1:{ProtocolInfo.DefaultPort}{ProtocolInfo.StreamPath}";

var subjectId = Environment.GetEnvironmentVariable("OPENLUO_SUBJECT_ID") ?? "builtin-rin";
var agentId = Environment.GetEnvironmentVariable("OPENLUO_AGENT_ID") ?? "companion";

try
{
    await new CliHubApp(hubUrl, subjectId, agentId).RunAsync(Console.In);
    return 0;
}
catch (HubConnectException ex)
{
    // 未起 Hub 是最常见的用法错误：给一行可读提示，而不是抛栈
    Console.Error.WriteLine(ex.Message);
    return 1;
}
