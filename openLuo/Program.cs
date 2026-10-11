using openLuo.Hosting;

// openLuo 服务端（独立程序）：内核 + Hub（HTTP 控制面 + WebSocket 数据面）。
// 本文件只做入口：解析参数 → 启动内核 → 运行 Hub → 关闭。
// 内核与应用装配都在 openLuo.Application（KernelStartup / HubStartup / OpenLuoBootstrapper）；
// 客户端是各自独立的程序（openluo-cli / openluo-tui / openluo-gui / openluo-qq）。
if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return args.Length == 0 ? 1 : 0;
}

if (!args.Contains("--serve"))
{
    Console.Error.WriteLine($"unknown arguments: {string.Join(' ', args)}");
    Console.Error.WriteLine("提示：客户端已独立为 openluo-cli / openluo-tui / openluo-gui / openluo-qq（各自连 Hub）。");
    PrintUsage();
    return 2;
}

var host = await OpenLuoBootstrapper.BootstrapAsync();
if (host is null) return 1;

await KernelStartup.PrepareAsync(host.ServiceProvider);

var listen = Environment.GetEnvironmentVariable("OPENLUO_HUB_LISTEN")
    ?? $"http://127.0.0.1:{openLuo.Protocol.ProtocolInfo.DefaultPort}";
await HubStartup.RunServeAsync(host.ServiceProvider, listen);

// 显式释放内核并给出可观测的关闭日志（不用 await using：需要在释放前后打点，见 LogStore 幂等释放说明）
Console.Error.WriteLine("[shutdown] hub stopped; disposing kernel");
await host.DisposeAsync();
Console.Error.WriteLine("[shutdown] kernel disposed");
return 0;

static void PrintUsage() => Console.WriteLine("""
用法: openLuo --serve

OpenLuo 服务端（Hub）：内核 + HTTP 控制面 + WebSocket 数据面。
  --serve          启动服务端
  -h, --help       显示本帮助

客户端是各自独立的程序（连 Hub，不启动内核）：
  openluo-cli      CLI 客户端        openluo-tui      TUI 客户端
  openluo-gui      GUI 客户端        openluo-qq       QQ 桥客户端

环境变量：
  OPENLUO_HUB_LISTEN   服务端监听地址（默认 http://127.0.0.1:8674）
  OPENLUO_HUB_URL      客户端连接的 Hub 地址（默认 ws://127.0.0.1:8674/v1/stream）
""");
