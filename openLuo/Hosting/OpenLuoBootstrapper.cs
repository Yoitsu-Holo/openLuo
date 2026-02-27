using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using openLuo.Core.Interfaces;
using openLuo.Infrastructure.IO;
using openLuo.Interfaces.QQbot;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Hosting;

public static class OpenLuoBootstrapper
{
    public static async Task<OpenLuoRuntimeContext?> BootstrapAsync(LaunchMode mode)
    {
        var logger = BootstrapLogger.Create(nameof(OpenLuoBootstrapper));
        var baseDir = AppContext.BaseDirectory;

        var configDir = Path.Combine(Directory.GetCurrentDirectory(), "config");
        if (!Directory.Exists(configDir))
        {
            logger.LogWarning("未找到配置目录 {ConfigDir}，请从 data/config/ 复制示例配置并编辑后重新启动。", configDir);
            return null;
        }

        var configLogger = BootstrapLogger.Create(nameof(RuntimeConfigCenter));
        var configCenter = new RuntimeConfigCenter(configDir, configLogger);
        var config = configCenter.GetSnapshot();
        WarnIfAnotherInstanceRunning(logger);

        if (string.IsNullOrEmpty(config.Llm.ApiKey))
        {
            logger.LogError("配置文件中 llm.apiKey 为空，请编辑 {ConfigPath}", configDir);
            configCenter.Dispose();
            return null;
        }

        if (config.Embedding.Enabled && string.IsNullOrEmpty(config.Embedding.ApiKey))
        {
            logger.LogError("配置文件中 embedding.apiKey 为空，请编辑 {ConfigPath}", configDir);
            configCenter.Dispose();
            return null;
        }

        var dbPath = string.IsNullOrEmpty(config.DatabasePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openLuo", "game.db")
            : config.DatabasePath;

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var services = new ServiceCollection()
            .AddOpenLuo(configCenter, baseDir);

        if (mode is LaunchMode.QqBot)
        {
            var qqBotConfigPath = ResolveQqBotConfigPath();
            if (qqBotConfigPath is null)
            {
                var defaultConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "qqbot.jsonc");
                logger.LogWarning("未找到 QQbot 配置文件，正在创建默认配置：{ConfigPath}", defaultConfigPath);
                CreateDefaultQqBotConfig(defaultConfigPath);
                logger.LogWarning("请编辑 QQbot 配置文件后重新启动。");
                configCenter.Dispose();
                return null;
            }

            services.AddSingleton<IQqBotConfigCenter>(_ => new QqBotConfigCenter(qqBotConfigPath));
        }

        if (mode is LaunchMode.Tui)
            services.AddSingleton<IGameStreams, TuiStreams>();
        else if (mode is LaunchMode.QqBot)
            services.AddSingleton<IGameStreams, NullGameStreams>();
        else
            services.AddSingleton<IGameStreams, ConsoleStreams>();

        var serviceProvider = services.BuildServiceProvider();
        try
        {
            var sessionCatalog = serviceProvider.GetRequiredService<IGameSessionCatalog>();
            var streams = serviceProvider.GetRequiredService<IGameStreams>();
            var preferredGameId = mode is LaunchMode.QqBot
                ? null
                : (await sessionCatalog.GetGameIdsAsync()).FirstOrDefault()?.GameId;
            var session = await sessionCatalog.OpenSessionAsync(new SessionOpenRequest
            {
                ClientType = mode switch
                {
                    LaunchMode.Tui => "tui",
                    LaunchMode.QqBot => "qqbot",
                    _ => "cli"
                },
                ClientId = mode switch
                {
                    LaunchMode.Tui => "default-tui",
                    LaunchMode.QqBot => "default-qqbot",
                    _ => "default-cli"
                },
                PreferredGameId = preferredGameId
            });
            var state = await session.TryGetStateAsync();

            return new OpenLuoRuntimeContext(
                configCenter,
                serviceProvider,
                sessionCatalog,
                session,
                streams,
                state);
        }
        catch
        {
            serviceProvider.Dispose();
            configCenter.Dispose();
            throw;
        }
    }

    static void CreateDefaultQqBotConfig(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var example = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "data", "config", "qqbot.example.jsonc"));
        File.WriteAllText(path, example);
    }

    static string? ResolveQqBotConfigPath()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "config", "qqbot.jsonc");
        return File.Exists(path) ? path : null;
    }

    static void WarnIfAnotherInstanceRunning(ILogger logger)
    {
        Process? current = null;
        try
        {
            current = Process.GetCurrentProcess();
            var currentPath = SafeGetMainModuleFileName(current);
            var candidates = Process.GetProcessesByName(current.ProcessName);
            var otherPids = new List<int>();

            foreach (var process in candidates)
            {
                try
                {
                    if (process.Id == current.Id) continue;
                    var candidatePath = SafeGetMainModuleFileName(process);
                    var samePath = !string.IsNullOrWhiteSpace(currentPath)
                        && !string.IsNullOrWhiteSpace(candidatePath)
                        && string.Equals(
                            Path.GetFullPath(candidatePath),
                            Path.GetFullPath(currentPath),
                            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                    if (samePath)
                        otherPids.Add(process.Id);
                }
                catch
                {
                    // ignore per-process inspection failures
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (otherPids.Count > 0)
            {
                logger.LogWarning("检测到已有 openLuo 进程在运行（pid: {Pids}）。", string.Join(", ", otherPids));
                logger.LogWarning("当前版本不支持多开，可能出现数据库并发写入冲突。");
            }
        }
        catch
        {
            // 启动检查失败不应影响游戏启动
        }
        finally
        {
            current?.Dispose();
        }
    }

    static string? SafeGetMainModuleFileName(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }
}
