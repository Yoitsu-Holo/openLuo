using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using openLuo.Capabilities.A2A;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Mcp;
using openLuo.Cli;
using openLuo.Client;
using openLuo.Composition;
using openLuo.Abstractions;
using openLuo.Hosting;
using openLuo.Interfaces.GUI;
using openLuo.Interfaces.QQbot;
using openLuo.Interfaces.TUI;
using System.Diagnostics;

var options = LaunchOptions.Parse(args);
if (options is null) return;

// Hub 鉴权配置来自环境变量（后续可迁至 server.jsonc）。
static openLuo.Server.HubAuthOptions BuildHubAuth()
{
    static bool Bool(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

    var keys = new Dictionary<string, string>(StringComparer.Ordinal);
    var raw = Environment.GetEnvironmentVariable("OPENLUO_HUB_API_KEYS");
    if (!string.IsNullOrWhiteSpace(raw))
    {
        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = pair.IndexOf(':');
            if (idx > 0)
                keys[pair[..idx]] = pair[(idx + 1)..];
        }
    }

    return new openLuo.Server.HubAuthOptions
    {
        AllowAnonymous = Bool("OPENLUO_HUB_ALLOW_ANONYMOUS", true),
        AnonymousRole = Environment.GetEnvironmentVariable("OPENLUO_HUB_ANONYMOUS_ROLE") ?? openLuo.Server.HubRoles.User,
        SharedSecret = Environment.GetEnvironmentVariable("OPENLUO_HUB_SECRET"),
        ApiKeys = keys,
        TokenTtlMinutes = int.TryParse(Environment.GetEnvironmentVariable("OPENLUO_HUB_TOKEN_TTL_MINUTES"), out var ttl) ? ttl : 720,
    };
}

// QQ 桥：经协议连 Hub（不启动内核）
if (options.Mode is LaunchMode.QqBot)
{
    var hubStream = Environment.GetEnvironmentVariable("OPENLUO_HUB_URL")
        ?? $"ws://127.0.0.1:{openLuo.Protocol.ProtocolInfo.DefaultPort}{openLuo.Protocol.ProtocolInfo.StreamPath}";
    var hubHttp = hubStream.Replace("ws://", "http://", StringComparison.Ordinal).Replace("/v1/stream", string.Empty, StringComparison.Ordinal);

    var qqConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "qqbot.jsonc");
    if (!File.Exists(qqConfigPath))
    {
        Console.Error.WriteLine($"QQbot config not found: {qqConfigPath}");
        return;
    }

    using var qqConfig = new QqBotConfigCenter(qqConfigPath);
    await using var qqHub = await HubClient.ConnectAsync(hubStream, "qq-bridge", "qq");
    using var qqHttp = new HttpClient(new SocketsHttpHandler { UseProxy = !new Uri(hubStream).IsLoopback }) { Timeout = TimeSpan.FromSeconds(30) };
    await new QqBotApplication(qqHub, qqHttp, hubHttp, qqConfig).RunAsync();
    return;
}
if (options.Mode is LaunchMode.Cli or LaunchMode.Tui or LaunchMode.Gui)
{
    var hubUrl = Environment.GetEnvironmentVariable("OPENLUO_HUB_URL")
        ?? $"ws://127.0.0.1:{openLuo.Protocol.ProtocolInfo.DefaultPort}{openLuo.Protocol.ProtocolInfo.StreamPath}";

    if (options.Mode is LaunchMode.Cli)
    {
        await new openLuo.Cli.CliHubApp(hubUrl, "builtin-rin", "companion").RunAsync(Console.In);
        return;
    }

    if (options.Mode is LaunchMode.Tui)
    {
        await new TuiApplication(hubUrl).RunAsync();
        return;
    }

    GuiApplication.Launch(hubUrl);
    return;
}

await using var host = await OpenLuoBootstrapper.BootstrapAsync(options.Mode);
if (host is null) return;
var serviceProvider = host.ServiceProvider;

// 连接远程能力源（MCP / A2A），配置缺失时为空集，不影响启动
// 并行连接：总耗时 ≈ 最慢单 server，而非串行 Σ。每个 server 独立 15s 超时，
// 失败仅标记不可用（IsHealthy=false），不阻塞其他 server，也不阻塞启动。
// 保持同步语义（连接完成后才进入 turn）：能力源 _tools/_client 无锁，
// 后台化会引入读写竞态，故不做 fire-and-forget。
const int McpConnectTimeoutSeconds = 15;
var bootwatch = Stopwatch.StartNew();
var mcpTotal = 0;
var mcpHealthy = 0;
var capabilitySources = serviceProvider.GetServices<ICapabilitySource>().ToList();
var connectTasks = capabilitySources.Select(async source =>
{
    switch (source)
    {
        case McpCapabilitySource mcp:
            Interlocked.Increment(ref mcpTotal);
            using (var mcpCts = new CancellationTokenSource(TimeSpan.FromSeconds(McpConnectTimeoutSeconds)))
            {
                try
                {
                    await mcp.ConnectAsync(mcpCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 超时取消：ConnectAsync 内部会清理客户端状态，走下方 IsHealthy 检查
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[mcp] server '{mcp.ProviderId}' connect error: {ex.Message}");
                }
                if (mcp.IsHealthy)
                    Interlocked.Increment(ref mcpHealthy);
                else
                    Console.Error.WriteLine($"[mcp] server '{mcp.ProviderId}' failed to connect; its capabilities are unavailable this session");
            }
            break;
        case A2ACapabilitySource a2a:
            try
            {
                await a2a.ConnectAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[a2a] agent '{a2a.ProviderId}' connect error: {ex.Message}");
            }
            if (!a2a.IsHealthy)
                Console.Error.WriteLine($"[a2a] agent '{a2a.ProviderId}' failed to connect; its capabilities are unavailable this session");
            break;
    }
});
await Task.WhenAll(connectTasks);

// 加载领域扩展（extensions/<id>/extension.jsonc + 程序集），填充注册表
var registry = serviceProvider.GetRequiredService<ExtensionRegistry>();
var extensionHost = new ExtensionHost(
    Path.Combine(AppContext.BaseDirectory, "extensions"),
    type => ActivatorUtilities.CreateInstance(serviceProvider, type));
var extensionResult = extensionHost.ScanAndLoad();
registry.SetExtensions(extensionResult.Loaded);
foreach (var diagnostic in extensionResult.Diagnostics.Where(d => !d.Loaded))
    Console.Error.WriteLine($"[extension] {diagnostic.ExtensionId}: {diagnostic.Error}");

// 组合根：目录/调度器/上下文在首次解析时读取已填充的注册表
var runtime = serviceProvider.GetRequiredService<IAgentRuntime>();

// 启动完成提示（BootstrapLogger，文本格式）：MCP 连接与扩展加载已就绪
var bootLogger = BootstrapLogger.Create("Startup");
bootLogger.LogInformation(
    "Startup complete: {McpHealthy}/{McpTotal} MCP server(s) connected, {ExtensionCount} extension(s) loaded, {ElapsedMs} ms",
    mcpHealthy, mcpTotal, extensionResult.Loaded.Count, bootwatch.ElapsedMilliseconds);

if (options.Mode is LaunchMode.Serve)
{
    var listen = Environment.GetEnvironmentVariable("OPENLUO_HUB_LISTEN")
        ?? $"http://127.0.0.1:{openLuo.Protocol.ProtocolInfo.DefaultPort}";
    var directory = new openLuo.Composition.CatalogRuntimeDirectory(
        serviceProvider.GetRequiredService<openLuo.Capabilities.Core.Models.ICapabilityCatalog>());
    var configService = new openLuo.Modules.AppShell.Application.JsonConfigService(
        Path.Combine(Directory.GetCurrentDirectory(), "config"));
    var hubStore = serviceProvider.GetRequiredService<openLuo.Infrastructure.Persistence.HubStore>();
    var jobs = new openLuo.Modules.AppShell.Application.InMemoryJobService(store: hubStore);
    await using var scheduler = new openLuo.Modules.AppShell.Application.InMemorySchedulerService(hubStore);
    var auth = BuildHubAuth();
    var logs = serviceProvider.GetService<openLuo.Core.Interfaces.ILogStore>();
    var assets = serviceProvider.GetService<openLuo.Core.Interfaces.IAssetStore>();
    var outputQueue = serviceProvider.GetService<openLuo.Capabilities.Core.IOutputQueue>();
    // 资产 TTL 清理（OPENLUO_ASSET_TTL_MINUTES > 0 时启用；仅清创建超期的资产）
    var assetTtlMinutes = int.TryParse(Environment.GetEnvironmentVariable("OPENLUO_ASSET_TTL_MINUTES"), out var ttlMinutes) ? ttlMinutes : 0;
    if (assetTtlMinutes > 0 && assets is not null)
    {
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
            while (await timer.WaitForNextTickAsync())
            {
                try { assets.PurgeExpired(TimeSpan.FromMinutes(assetTtlMinutes)); }
                catch { /* 清理失败不影响服务 */ }
            }
        });
    }

    var confirmationGate = serviceProvider.GetRequiredService<openLuo.Server.HubConfirmationGate>();
    await openLuo.Server.HubServer.RunAsync(runtime, new openLuo.Server.HubServerOptions { Listen = listen }, directory, configService, jobs, scheduler, logs, auth, assets, hubStore, outputQueue, hubStore, confirmationGate);
    return;
}
