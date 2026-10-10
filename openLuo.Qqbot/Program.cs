using openLuo.Client;
using openLuo.Interfaces.QQbot;
using openLuo.Protocol;

// openLuo QQ 桥客户端（独立进程）：经协议连 Hub（**不启动内核**），只依赖 Protocol + Client + OneBot。
var hubStream = Environment.GetEnvironmentVariable("OPENLUO_HUB_URL")
    ?? $"ws://127.0.0.1:{ProtocolInfo.DefaultPort}{ProtocolInfo.StreamPath}";
var hubHttp = hubStream
    .Replace("ws://", "http://", StringComparison.Ordinal)
    .Replace("/v1/stream", string.Empty, StringComparison.Ordinal);

var qqConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "qqbot.jsonc");
if (!File.Exists(qqConfigPath))
{
    Console.Error.WriteLine($"QQbot config not found: {qqConfigPath}");
    Console.Error.WriteLine("提示：先复制示例配置并编辑 —— cp data/config/qqbot.example.jsonc config/qqbot.jsonc");
    return 1;
}

using var qqConfig = new QqBotConfigCenter(qqConfigPath);
using var qqHttp = new HttpClient(new SocketsHttpHandler { UseProxy = !new Uri(hubStream).IsLoopback })
{
    Timeout = TimeSpan.FromSeconds(30),
};

// QQ 桥是长期连接：Hub 未起/中途重启都自己重连（QqBotApplication.EnsureHubAsync），故这里只传连接工厂
await new QqBotApplication(
    ct => HubClient.ConnectAsync(hubStream, "qq-bridge", "qq", ct: ct),
    qqHttp, hubHttp, qqConfig).RunAsync();
return 0;
