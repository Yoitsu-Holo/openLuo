using Microsoft.Extensions.DependencyInjection;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Core.Definitions;
using openLuo.Infrastructure.Database;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Infrastructure.IO;
using openLuo.playgraound.Infrastructure;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace openLuo.playgraound.Demos.Content;

internal static class ContentBootstrapDemo
{
    public static async Task<int> RunAsync()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("content-bootstrap demo failed.");
            Console.Error.WriteLine("reason: could not locate repository root from current process path.");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IGameStreams, ConsoleStreams>();
        services.AddOpenLuo(
            new AppConfig
            {
                DatabasePath = Path.Combine(Path.GetTempPath(), $"openluo-content-bootstrap-{Guid.NewGuid():N}.db"),
                SqliteVec = new SqliteVecConfig
                {
                    ExtensionPath = ResolveSqliteVecLibraryPath(repoRoot),
                    VectorDimensions = 1536
                },
                Llm = new LlmConfig
                {
                    ApiKey = "test-key",
                    BaseUrl = "https://example.invalid/v1/",
                    Provider = LlmProvider.OpenAI,
                    Model = "gpt-test"
                }
            },
            repoRoot);

        await using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ContentRegistry>();
        var bootstrapper = provider.GetRequiredService<ISessionBootstrapper>();
        var databaseInitializer = provider.GetRequiredService<DatabaseInitializer>();
        var stateRepo = provider.GetRequiredService<IGameStateRepository>();
        var characterRepo = provider.GetRequiredService<ICharacterRepository>();
        var stateStore = provider.GetRequiredService<IStateStore>();

        await databaseInitializer.InitializeAsync();

        var archetypes = registry.GetAll<CharacterArchetypeDefinition>();
        var selectedArchetype = archetypes.FirstOrDefault(x => string.Equals(x.Id, "builtin-nekomimi", StringComparison.OrdinalIgnoreCase))
            ?? archetypes.FirstOrDefault();
        if (selectedArchetype is null)
        {
            Console.Error.WriteLine("content-bootstrap demo failed.");
            Console.Error.WriteLine("reason: no character archetype found in ContentRegistry.");
            return 1;
        }

        Console.WriteLine("=== Content Bootstrap Demo ===");
        Console.WriteLine("flow: raw content -> ContentRegistry -> PluginConfigMerge -> SessionBootstrapper -> persisted state");
        Console.WriteLine();

        PrintRegistrySummary(registry);
        Console.WriteLine($"selectedArchetype: {selectedArchetype.Id} ({selectedArchetype.DisplayName})");
        Console.WriteLine();

        PrintMergedPluginConfig(registry, "builtin_char_status_relationship", selectedArchetype.Id);

        var sessionId = $"bootstrap-demo-{Guid.NewGuid():N}";
        var bootstrapResult = await bootstrapper.BootstrapAsync(new SessionBootstrapRequest
        {
            SessionId = sessionId,
            PlayerName = "玩家",
            SelectedCharacterIds = [selectedArchetype.Id],
            ActiveCharacterId = $"char_{selectedArchetype.Id.Trim().ToLowerInvariant()}",
            ResourceOverrides = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
            {
                ["char_status.trust"] = 88
            },
            SharedMemorySeeds =
            [
                "这是 content-bootstrap demo 写入的一条共享记忆。"
            ],
            PrivateMemorySeedsByCharacter = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [selectedArchetype.Id] =
                [
                    "这是为当前角色写入的一条私有初始化记忆。"
                ]
            }
        });

        Console.WriteLine("=== Bootstrap Result ===");
        Console.WriteLine($"sessionId: {bootstrapResult.SessionId}");
        Console.WriteLine($"gameId: {bootstrapResult.GameId}");
        Console.WriteLine($"archetypeId: {bootstrapResult.ArchetypeId}");
        Console.WriteLine($"activeCharacterId: {bootstrapResult.ActiveCharacterId}");
        Console.WriteLine("characters:");
        foreach (var character in bootstrapResult.Characters)
            Console.WriteLine($"- {character.CharacterId} -> {character.DisplayName} (archetype={character.ArchetypeId}, location={character.InitialLocation})");
        Console.WriteLine("resources:");
        foreach (var resource in bootstrapResult.Resources.Values.OrderBy(x => x.ResourceId, StringComparer.OrdinalIgnoreCase).Take(6))
            Console.WriteLine($"- {resource.ResourceId} = {resource.Value} (owner={resource.OwnerKind})");
        if (bootstrapResult.Diagnostics.Count > 0)
        {
            Console.WriteLine("diagnostics:");
            foreach (var diagnostic in bootstrapResult.Diagnostics)
                Console.WriteLine($"- {diagnostic.Code}: {diagnostic.Message}");
        }
        else
        {
            Console.WriteLine("diagnostics: <none>");
        }
        Console.WriteLine();

        var persistedState = await stateRepo.GetAsync();
        Console.WriteLine("=== Persisted GameState ===");
        Console.WriteLine($"id: {persistedState?.Id}");
        Console.WriteLine($"playerName: {persistedState?.PlayerName}");
        Console.WriteLine($"archetypeId: {persistedState?.ArchetypeId}");
        Console.WriteLine($"activeCharacterId: {persistedState?.ActiveCharacterId}");
        Console.WriteLine($"currentLocation: {persistedState?.CurrentLocation}");
        Console.WriteLine();

        var persistedCharacters = await characterRepo.ListByGameIdAsync(sessionId);
        Console.WriteLine("=== Persisted Characters ===");
        foreach (var character in persistedCharacters)
            Console.WriteLine($"- {character.Id} | archetype={character.ArchetypeId} | name={character.Name}");
        Console.WriteLine();

        if (persistedState is not null && !string.IsNullOrWhiteSpace(bootstrapResult.ActiveCharacterId))
        {
            var trust = await stateStore.GetRawAsync(
                persistedState.Id,
                StateOwnerKind.Character,
                bootstrapResult.ActiveCharacterId,
                "char_status",
                "trust");
            Console.WriteLine("=== Persisted Resource Check ===");
            Console.WriteLine($"char_status.trust[{bootstrapResult.ActiveCharacterId}] = {trust ?? "<null>"}");
        }

        return 0;
    }

    private static void PrintRegistrySummary(ContentRegistry registry)
    {
        Console.WriteLine("=== Registry Summary ===");
        Console.WriteLine($"characters: {registry.GetAll<CharacterArchetypeDefinition>().Count}");
        Console.WriteLine($"resources: {registry.GetAll<ResourceDefinition>().Count}");
        Console.WriteLine($"items: {registry.GetAll<ItemDefinition>().Count}");
        Console.WriteLine($"tools: {registry.GetAll<ToolDefinition>().Count}");
        Console.WriteLine($"skills: {registry.GetAll<SkillDefinition>().Count}");
        Console.WriteLine($"pluginDefaultConfigs: {registry.GetAll<PluginDefaultConfigDefinition>().Count}");
        Console.WriteLine($"pluginCharacterOverrides: {registry.GetAll<PluginCharacterConfigOverrideDefinition>().Count}");
        Console.WriteLine($"packs: {registry.GetAll<PackManifest>().Count}");
        Console.WriteLine();
    }

    private static void PrintMergedPluginConfig(ContentRegistry registry, string pluginId, string characterId)
    {
        Console.WriteLine("=== Merged Plugin Config ===");
        Console.WriteLine($"pluginId: {pluginId}");
        Console.WriteLine($"characterId: {characterId}");
        if (!registry.TryGetMergedPluginConfig(pluginId, characterId, out var merged) || merged is null)
        {
            Console.WriteLine("<none>");
            Console.WriteLine();
            return;
        }

        Console.WriteLine(merged.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine();
    }

    private static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var dataDir = Path.Combine(current.FullName, "openLuo", "data");
            if (Directory.Exists(dataDir))
                return Path.Combine(current.FullName, "openLuo");

            var directDataDir = Path.Combine(current.FullName, "data");
            if (Directory.Exists(directDataDir))
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }

    private static string ResolveSqliteVecLibraryPath(string repoRoot)
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "vec0.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "vec0.dylib"
                : "vec0.so";

        var rid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "win-x64"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "osx-x64"
                : "linux-x64";

        var candidate = Path.Combine(repoRoot, "native", "sqlite-vec", rid, fileName);
        if (!File.Exists(candidate))
            throw new FileNotFoundException($"sqlite-vec dynamic library not found: {candidate}");
        return candidate;
    }
}
