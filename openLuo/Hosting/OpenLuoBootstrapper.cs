using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using openLuo.Core.Interfaces;
using openLuo.Infrastructure.IO;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Hosting;

public static class OpenLuoBootstrapper
{
    /// <summary>启动内核并组装服务提供者（服务端专用；客户端进程不启动内核）。</summary>
    public static async Task<OpenLuoRuntimeContext?> BootstrapAsync()
    {
        var logger = BootstrapLogger.Create(nameof(OpenLuoBootstrapper));
        var baseDir = AppContext.BaseDirectory;

        var configDir = Path.Combine(Directory.GetCurrentDirectory(), "config");
        if (!Directory.Exists(configDir))
        {
            logger.LogWarning("Config directory not found: {ConfigDir}. Copy example configs from data/config/, edit, and restart.", configDir);
            return null;
        }

        var configLogger = BootstrapLogger.Create(nameof(RuntimeConfigCenter));
        var configCenter = new RuntimeConfigCenter(configDir, configLogger);
        var config = configCenter.GetSnapshot();
        openLuo.Infrastructure.Config.Config.Initialize(config);
        WarnIfAnotherInstanceRunning(logger);

        var enabledLlmRoutes = config.Llm.Routes.Where(route => route.Enabled).ToList();
        if (enabledLlmRoutes.Count == 0)
        {
            logger.LogError("No enabled LLM route in config llm.routes. Edit {ConfigPath}", configDir);
            configCenter.Dispose();
            return null;
        }

        var missingApiKeyRoute = enabledLlmRoutes.FirstOrDefault(route =>
            route.Provider != LlmProvider.Ollama && string.IsNullOrWhiteSpace(route.ApiKey));
        if (missingApiKeyRoute is not null)
        {
            logger.LogError("llm.routes[{RouteName}].apiKey is empty. Edit {ConfigPath}",
                string.IsNullOrWhiteSpace(missingApiKeyRoute.Name) ? missingApiKeyRoute.Model : missingApiKeyRoute.Name,
                configDir);
            configCenter.Dispose();
            return null;
        }

        if (config.Embedding.Enabled && string.IsNullOrEmpty(config.Embedding.ApiKey))
        {
            logger.LogError("embedding.apiKey is empty. Edit {ConfigPath}", configDir);
            configCenter.Dispose();
            return null;
        }

        var dbPath = string.IsNullOrEmpty(config.DatabasePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openLuo", "game.db")
            : config.DatabasePath;

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var services = new ServiceCollection()
            .AddOpenLuo(config, baseDir);

        services.AddSingleton<IGameStreams, ConsoleStreams>();
        RegisterRemoteCapabilitySources(services, configDir, logger);

        var serviceProvider = services.BuildServiceProvider();

        // 初始化静态日志系统 — 此后全局 Logger.Xxx 调用都走 GameLogger
        var gameLogger = serviceProvider.GetRequiredService<openLuo.Infrastructure.Logging.GameLogger>();
        openLuo.Infrastructure.Logging.Logger.Initialize(gameLogger);

        try
        {
            var streams = serviceProvider.GetRequiredService<IGameStreams>();

            return new OpenLuoRuntimeContext(
                serviceProvider,
                streams);
        }
        catch
        {
            serviceProvider.Dispose();
            configCenter.Dispose();
            throw;
        }
    }

    /// <summary>按 config/mcp-servers.jsonc / config/a2a-agents.jsonc 注册远程能力源（文件缺失则跳过）。</summary>
    static void RegisterRemoteCapabilitySources(IServiceCollection services, string configDir, ILogger logger)
    {
        var mcpPath = Path.Combine(configDir, "mcp-servers.jsonc");
        if (File.Exists(mcpPath))
        {
            try
            {
                var config = LoadJsonc<openLuo.Capabilities.Mcp.McpServersConfig>(mcpPath);
                foreach (var server in config.Servers)
                    services.AddSingleton<openLuo.Capabilities.Core.ICapabilitySource>(new openLuo.Capabilities.Mcp.McpCapabilitySource(server));
                logger.LogInformation("Registered {Count} MCP server(s) from {File}", config.Servers.Count, Path.GetFileName(mcpPath));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load MCP servers config: {Path}", mcpPath);
            }
        }

        var a2aPath = Path.Combine(configDir, "a2a-agents.jsonc");
        if (File.Exists(a2aPath))
        {
            try
            {
                var config = LoadJsonc<openLuo.Capabilities.A2A.A2AAgentsConfig>(a2aPath);
                foreach (var agent in config.Agents)
                    services.AddSingleton<openLuo.Capabilities.Core.ICapabilitySource>(new openLuo.Capabilities.A2A.A2ACapabilitySource(agent));
                logger.LogInformation("Registered {Count} A2A agent(s) from {File}", config.Agents.Count, Path.GetFileName(a2aPath));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load A2A agents config: {Path}", a2aPath);
            }
        }
    }

    static T LoadJsonc<T>(string path) where T : class, new() =>
        System.Text.Json.JsonSerializer.Deserialize<T>(
            File.ReadAllText(path),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new T();

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
                logger.LogWarning("Detected an existing openLuo process running (pid: {Pids}).", string.Join(", ", otherPids));
                logger.LogWarning("This version does not support multiple instances; concurrent DB writes may conflict.");
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
