using Microsoft.Extensions.DependencyInjection;
using openLuo.Capabilities.Core;
using openLuo.Composition;
using openLuo.Core.Interfaces;

namespace openLuo.Hosting;

/// <summary>
/// Hub 启动编排：装配服务端所需组件（运行时门面、能力目录、配置服务、作业/调度、
/// 鉴权、日志、资产、输出队列、轨迹、确认闸），并运行 <c>openLuo.Server.HubServer</c>。
/// 宿主 exe 只负责传入监听地址与取消令牌。
/// </summary>
public static class HubStartup
{
    public static async Task RunServeAsync(IServiceProvider serviceProvider, string listen, CancellationToken ct = default)
    {
        // 组合根：目录/调度器/上下文在首次解析时读取已填充的注册表
        var runtime = serviceProvider.GetRequiredService<IAgentRuntime>();

        var directory = new CatalogRuntimeDirectory(
            serviceProvider.GetRequiredService<openLuo.Capabilities.Core.Models.ICapabilityCatalog>());
        var configService = new openLuo.Modules.AppShell.Application.JsonConfigService(
            Path.Combine(Directory.GetCurrentDirectory(), "config"));
        var hubStore = serviceProvider.GetRequiredService<openLuo.Infrastructure.Persistence.HubStore>();
        var jobs = new openLuo.Modules.AppShell.Application.InMemoryJobService(store: hubStore);
        await using var scheduler = new openLuo.Modules.AppShell.Application.InMemorySchedulerService(hubStore);
        var auth = BuildHubAuth();
        var logs = serviceProvider.GetService<ILogStore>();
        var assets = serviceProvider.GetService<IAssetStore>();
        var outputQueue = serviceProvider.GetService<IOutputQueue>();

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

        // 世界状态只读端口：定义在 Capabilities（Server 允许依赖的两层之一），由内核的状态事务实现；
        // Hub 只做"按会话取 subjectId → 读状态"的投影，不感知具体实现。
        var stateReader = serviceProvider.GetService<openLuo.Capabilities.Core.IStateReader>();

        await openLuo.Server.HubServer.RunAsync(
            runtime,
            new openLuo.Server.HubServerOptions { Listen = listen, Security = BuildSecurityPolicy() },
            directory, configService, jobs, scheduler, logs, auth, assets, hubStore, outputQueue, hubStore, confirmationGate,
            stateReader,
            ct);
    }

    /// <summary>
    /// Hub 入口治理策略（§4.9）：从 <c>security.jsonc</c> 的 <c>security</c> 段映射
    /// （经静态门面 <see cref="openLuo.Infrastructure.Config.Config"/>，它由 <c>RuntimeConfigCenter</c> 热重载）。
    /// 返回**闭包**而非快照值，配合 Server 侧每 30 次检查的惰性刷新，改配置无需重启。
    /// </summary>
    private static Func<openLuo.Server.Security.HubSecurityPolicy> BuildSecurityPolicy() => () =>
    {
        var security = openLuo.Infrastructure.Config.Config.Security;
        return new openLuo.Server.Security.HubSecurityPolicy(
            Enabled: security.HubEnabled,
            RequestsPerMinute: security.HubRequestsPerMinute,
            BurstLimit: security.HubBurstLimit,
            MaxInputLength: security.HubMaxInputLength,
            MaxFrameBytes: security.HubMaxFrameBytes,
            StrictInputValidation: security.HubStrictInputValidation);
    };

    /// <summary>Hub 鉴权配置来自环境变量（后续可迁至 server.jsonc）。</summary>
    private static openLuo.Server.HubAuthOptions BuildHubAuth()
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
}
