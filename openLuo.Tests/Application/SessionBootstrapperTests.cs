using Microsoft.Extensions.DependencyInjection;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Application.Validation;
using openLuo.Modules.Content.Core.Definitions;
using openLuo.Modules.SessionRuntime.Application;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;
using System.Runtime.InteropServices;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Application.Tests;

public sealed class SessionBootstrapperTests
{
    [Fact]
    public async Task BootstrapAsync_WithoutSelection_MaterializesAllCharactersAndResources()
    {
        var stateRepo = Substitute.For<IGameStateRepository>();
        var characterRepo = Substitute.For<ICharacterRepository>();
        var stateStore = Substitute.For<IStateStore>();
        var memoryWriteService = Substitute.For<IMemoryWriteService>();
        var registry = BuildRegistry(
            backgrounds:
            [
                new CharacterArchetypeDefinition
                {
                    Id = "bg.rin",
                    DisplayName = "校园",
                    CharacterName = "汐泠",
                    InitialLocation = "教室"
                },
                new CharacterArchetypeDefinition
                {
                    Id = "bg.aya",
                    DisplayName = "社团",
                    CharacterName = "艾娅",
                    InitialLocation = "活动室"
                }
            ],
            resources:
            [
                new ResourceDefinition
                {
                    Id = "game_resource.gold",
                    DisplayName = "金币",
                    ResourceType = "number",
                    InitialValue = 30,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["owner_kind"] = "game"
                    }
                }
            ]);
        characterRepo.ListByGameIdAsync("session-1", Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<Character>>(
            [
                new Character { Id = "char_bg.rin", GameId = "session-1", ArchetypeId = "bg.rin", Name = "汐泠" },
                new Character { Id = "char_bg.aya", GameId = "session-1", ArchetypeId = "bg.aya", Name = "艾娅" }
            ]));
        var sut = new ContentRegistrySessionBootstrapper(registry, stateRepo, characterRepo, stateStore, memoryWriteService);

        var result = await sut.BootstrapAsync(new SessionBootstrapRequest
        {
            SessionId = "session-1"
        });

        Assert.Equal("session-1", result.SessionId);
        Assert.Equal("session-1", result.GameId);
        Assert.Equal(2, result.Characters.Count);
        Assert.Equal("char_bg.rin", result.ActiveCharacterId);
        Assert.Contains(result.Characters, x => x.CharacterId == "char_bg.rin" && x.DisplayName == "汐泠" && x.InitialLocation == "教室");
        Assert.Contains(result.Resources, pair => pair.Key == "game_resource.gold" && pair.Value.Value == 30m);
        Assert.Empty(result.Diagnostics);
        await stateRepo.Received(1).SaveAsync(Arg.Is<GameState>(x =>
            x.Id == "session-1" &&
            x.ActiveCharacterId == "char_bg.rin" &&
            x.ArchetypeId == "bg.rin"), Arg.Any<CancellationToken>());
        await characterRepo.Received(2).SaveAsync(Arg.Any<Character>(), Arg.Any<CancellationToken>());
        await stateStore.Received(1).SetBatchAsync(Arg.Any<IEnumerable<(string gameId, StateOwnerKind ownerKind, string ownerId, string @namespace, string key, string value)>>());
    }

    [Fact]
    public async Task BootstrapAsync_WithSelectionAndOverrides_AppliesRequestedMaterializationAndDiagnostics()
    {
        var stateRepo = Substitute.For<IGameStateRepository>();
        var characterRepo = Substitute.For<ICharacterRepository>();
        var stateStore = Substitute.For<IStateStore>();
        var memoryWriteService = Substitute.For<IMemoryWriteService>();
        var registry = BuildRegistry(
            backgrounds:
            [
                new CharacterArchetypeDefinition
                {
                    Id = "bg.rin",
                    DisplayName = "汐泠原型",
                    CharacterName = "汐泠"
                },
                new CharacterArchetypeDefinition
                {
                    Id = "bg.aya",
                    DisplayName = "艾娅原型",
                    CharacterName = "艾娅"
                }
            ],
            resources:
            [
                new ResourceDefinition
                {
                    Id = "char_status.trust",
                    DisplayName = "信任度",
                    ResourceType = "number",
                    InitialValue = 50,
                    MinValue = 0,
                    MaxValue = 100,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["owner_kind"] = "character"
                    }
                }
            ]);
        characterRepo.ListByGameIdAsync("session-2", Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<Character>>(
            [
                new Character { Id = "char_bg.aya", GameId = "session-2", ArchetypeId = "bg.aya", Name = "艾娅" }
            ]));
        var sut = new ContentRegistrySessionBootstrapper(registry, stateRepo, characterRepo, stateStore, memoryWriteService);

        var result = await sut.BootstrapAsync(new SessionBootstrapRequest
        {
            SessionId = "session-2",
            SelectedCharacterIds = ["bg.aya", "missing-character"],
            ActiveCharacterId = "missing-active",
            ResourceOverrides = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
            {
                ["char_status.trust"] = 120,
                ["missing.resource"] = 1
            }
        });

        var character = Assert.Single(result.Characters);
        Assert.Equal("char_bg.aya", character.CharacterId);
        Assert.Equal("char_bg.aya", result.ActiveCharacterId);
        Assert.Equal(120m, result.Resources["char_status.trust"].Value);
        Assert.Contains(result.Diagnostics, x => x.Code == "character.not_found");
        Assert.Contains(result.Diagnostics, x => x.Code == "active_character.not_found");
        Assert.Contains(result.Diagnostics, x => x.Code == "resource.override_out_of_range");
        Assert.Contains(result.Diagnostics, x => x.Code == "resource.not_found");
    }

    [Fact]
    public async Task BootstrapAsync_Persists_Backstory_And_CustomMemorySeeds()
    {
        var stateRepo = Substitute.For<IGameStateRepository>();
        var characterRepo = Substitute.For<ICharacterRepository>();
        var stateStore = Substitute.For<IStateStore>();
        var memoryWriteService = Substitute.For<IMemoryWriteService>();

        var registry = BuildRegistry(
            backgrounds:
            [
                new CharacterArchetypeDefinition
                {
                    Id = "bg.rin",
                    DisplayName = "汐泠原型",
                    CharacterName = "汐泠",
                    Backstory = "她一直住在玩家家里。"
                }
            ],
            resources: []);

        characterRepo.ListByGameIdAsync("session-3", Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<Character>>(
            [
                new Character { Id = "char_bg.rin", GameId = "session-3", ArchetypeId = "bg.rin", Name = "汐泠" }
            ]));

        memoryWriteService.WriteAsync(Arg.Any<MemoryWriteInput>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryWriteResult { Success = true });

        var sut = new ContentRegistrySessionBootstrapper(registry, stateRepo, characterRepo, stateStore, memoryWriteService);

        await sut.BootstrapAsync(new SessionBootstrapRequest
        {
            SessionId = "session-3",
            SharedMemorySeeds = ["花园钥匙通常由女仆保管。"],
            PrivateMemorySeedsByCharacter = new Dictionary<string, IReadOnlyList<string>>
            {
                ["bg.rin"] = ["她非常依恋玩家。"]
            }
        });

        await memoryWriteService.Received().WriteAsync(
            Arg.Is<MemoryWriteInput>(x =>
                x.GameId == "session-3" &&
                x.Scope == MemoryScope.Shared &&
                x.RawContent.Contains("花园钥匙通常由女仆保管")),
            Arg.Any<CancellationToken>());
        await memoryWriteService.Received().WriteAsync(
            Arg.Is<MemoryWriteInput>(x =>
                x.GameId == "session-3" &&
                x.CharacterId == "char_bg.rin" &&
                x.Scope == MemoryScope.CharacterPrivate &&
                x.RawContent.Contains("一直住在玩家家里")),
            Arg.Any<CancellationToken>());
        await memoryWriteService.Received().WriteAsync(
            Arg.Is<MemoryWriteInput>(x =>
                x.GameId == "session-3" &&
                x.CharacterId == "char_bg.rin" &&
                x.Scope == MemoryScope.CharacterPrivate &&
                x.RawContent.Contains("非常依恋玩家")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddOpenLuo_RegistersSessionBootstrapper()
    {
        var baseDir = CreateBaseDir();
        try
        {
            var services = new ServiceCollection()
                .AddSingleton<IGameStreams>(_ => Substitute.For<IGameStreams>())
                .AddOpenLuo(
                    new AppConfig
                    {
                        DatabasePath = Path.Combine(baseDir, "bootstrap.db"),
                        SqliteVec = new SqliteVecConfig
                        {
                            ExtensionPath = ResolveSqliteVecLibraryPath(),
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
                    baseDir);

            using var provider = services.BuildServiceProvider();
            var bootstrapper = provider.GetRequiredService<ISessionBootstrapper>();

            Assert.IsType<ContentRegistrySessionBootstrapper>(bootstrapper);
        }
        finally
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, recursive: true);
        }
    }

    private static ContentRegistry BuildRegistry(
        IReadOnlyList<CharacterArchetypeDefinition> backgrounds,
        IReadOnlyList<ResourceDefinition> resources)
    {
        var builder = new ContentRegistryBuilder(new BasicContentValidator());
        var pack = new PackManifest
        {
            Id = "test.archetypes",
            DisplayName = "Test Archetypes",
            Contents = backgrounds.Select(background => new PackContentRef
            {
                Kind = ContentKind.CharacterArchetype,
                Id = background.Id
            }).ToList()
        };
        builder.AddPack(pack);

        foreach (var background in backgrounds)
            builder.AddDefinition(background, pack.Id);

        foreach (var resource in resources)
            builder.AddDefinition(resource, "plugin.resources");

        return builder.Build();
    }

    private static string CreateBaseDir()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"openluo-bootstrap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(baseDir, "data", "archetypes"));
        Directory.CreateDirectory(Path.Combine(baseDir, "data", "plugins"));
        File.WriteAllText(Path.Combine(baseDir, "data", "archetypes", "school.jsonc"), """
        {
          "id": "school",
          "name": "校园",
          "characterName": "铃"
        }
        """);
        return baseDir;
    }

    private static string ResolveSqliteVecLibraryPath()
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

        var outputCandidate = Path.Combine(
            AppContext.BaseDirectory,
            "native",
            "sqlite-vec",
            rid,
            fileName);

        if (File.Exists(outputCandidate)) return outputCandidate;

        var repoCandidate = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "openLuo",
            "native",
            "sqlite-vec",
            rid,
            fileName));

        Assert.True(File.Exists(repoCandidate), $"sqlite-vec dynamic library not found: {repoCandidate}");
        return repoCandidate;
    }
}
