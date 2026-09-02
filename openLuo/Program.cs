using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using openLuo.Capabilities.A2A;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Mcp;
using openLuo.Cli;
using openLuo.Composition;
using openLuo.Abstractions;
using openLuo.Hosting;
using openLuo.Interfaces.GUI;
using openLuo.Interfaces.QQbot;
using openLuo.Interfaces.TUI;
using System.Diagnostics;

var options = LaunchOptions.Parse(args);
if (options is null) return;

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

if (options.Mode is LaunchMode.Tui)
{
    await new TuiApplication(runtime).RunAsync();
    return;
}
if (options.Mode is LaunchMode.QqBot)
{
    await new QqBotApplication(
        runtime,
        serviceProvider.GetRequiredService<IQqBotConfigCenter>(),
        serviceProvider.GetRequiredService<openLuo.Capabilities.Core.IOutputQueue>(),
        serviceProvider.GetService<openLuo.Core.Interfaces.IGameLogger>()).RunAsync();
    return;
}
if (options.Mode is LaunchMode.Gui)
{
    GuiApplication.Launch(runtime);
    return;
}

var session = await runtime.OpenSessionAsync(new SessionOpenRequest
{
    SessionId = "cli-session", SubjectId = "builtin-rin", AgentId = "companion", ClientType = "cli", ClientId = "local"
});
await new openLuo.Cli.CliApplication(runtime).RunAsync(session, Console.In);
