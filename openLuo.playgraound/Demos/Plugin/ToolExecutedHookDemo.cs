using Microsoft.Extensions.DependencyInjection;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models.HookContexts;
using openLuo.Modules.PluginRuntime.Infrastructure;
using openLuo.playgraound.Infrastructure;

namespace openLuo.playgraound.Demos.Plugin;

internal static class ToolExecutedHookDemo
{
    public static async Task<int> RunAsync()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("tool-executed-hook demo failed.");
            Console.Error.WriteLine("reason: could not locate repository root.");
            return 1;
        }

        var demoPluginsDir = Path.Combine(repoRoot, "openLuo.playgraound", "Demos", "Plugin", "demo_plugins");
        if (!Directory.Exists(demoPluginsDir))
        {
            Console.Error.WriteLine("tool-executed-hook demo failed.");
            Console.Error.WriteLine($"reason: plugin directory not found: {demoPluginsDir}");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IGameLogger, ConsoleGameLogger>();
        services.AddSingleton<IRuntimeConfigCenter>(_ => new StaticRuntimeConfigCenter(new AppConfig()));
        services.AddSingleton<IAgentFlowRegistry, DefaultAgentFlowRegistry>();
        services.AddSingleton<IGameBridgeContextAccessor, AsyncLocalGameBridgeContextAccessor>();

        await using var provider = services.BuildServiceProvider();

        var host = new McpPluginHost(
            baseDir: repoRoot,
            logger: provider.GetRequiredService<IGameLogger>(),
            configCenter: provider.GetRequiredService<IRuntimeConfigCenter>(),
            flowRegistry: provider.GetRequiredService<IAgentFlowRegistry>(),
            bridgeContextAccessor: provider.GetRequiredService<IGameBridgeContextAccessor>());

        await host.LoadAllAsync(demoPluginsDir);

        Console.WriteLine("=== Tool Executed Hook Demo ===");
        Console.WriteLine("flow: host -> McpPluginHost -> onToolExecuted -> demo plugin");
        Console.WriteLine();

        var result = await host.CallToolExecutedHookAsync(new OnToolExecutedInput
        {
            GameId = "playground-tool-hook",
            SessionId = "playground-session",
            CharacterId = "char_builtin-rin",
            ArchetypeId = "builtin-rin",
            ToolName = "fetch_random_image",
            ExecutorKind = "core",
            Success = true,
            Args = [],
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            OutputText = "给你找来了一张图。",
            AssetIds = ["asset_demo_image_001"],
            MimeTypes = ["image/png"],
            PresentationProfile = "Default"
        });

        Console.WriteLine("=== Hook Output ===");
        Console.WriteLine($"additionalText: {result.AdditionalText ?? "<none>"}");
        if (result.Notices is { Count: > 0 })
        {
            Console.WriteLine("notices:");
            foreach (var notice in result.Notices)
                Console.WriteLine($"- {notice}");
        }
        else
        {
            Console.WriteLine("notices: <none>");
        }

        Console.WriteLine();
        Console.WriteLine("--- ImageBlock mapping demo ---");
        Console.WriteLine("Hook input AssetIds/MimeTypes can be mapped to unified Blocks:");
        var demoImage = new ImageBlock
        {
            Kind = BlockKind.Image,
            AssetId = "asset_demo_image_001",
            MimeType = "image/png",
            Name = "demo image",
            Caption = "给你找来了一张图。"
        };
        Console.WriteLine($"  [ImageBlock] assetId={demoImage.AssetId} mime={demoImage.MimeType} name={demoImage.Name}");
        Console.WriteLine();

        await host.DisposeAsync();
        return 0;
    }

    private static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "openLuo")) &&
                Directory.Exists(Path.Combine(current.FullName, "openLuo.playgraound")))
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }
}
