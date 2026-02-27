using openLuo.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace openLuo.Application.Tests;

public sealed class SessionRuntimeSurfaceTests : IDisposable
{
    private readonly string _baseDir = Path.Combine(
        Path.GetTempPath(),
        $"gimai-session-runtime-{Guid.NewGuid():N}");

    public SessionRuntimeSurfaceTests()
    {
        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(Path.Combine(_baseDir, "data", "backgrounds"));
        Directory.CreateDirectory(Path.Combine(_baseDir, "data", "plugins"));
        File.WriteAllText(Path.Combine(_baseDir, "data", "backgrounds", "school.jsonc"), """
        {
          "id": "school",
          "name": "校园",
          "characterName": "铃"
        }
        """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
            Directory.Delete(_baseDir, recursive: true);
    }

    [Fact]
    public async Task AddOpenLuo_CanResolveSessionSurfaces()
    {
        var services = new ServiceCollection()
            .AddSingleton<IGameStreams>(_ => Substitute.For<IGameStreams>())
            .AddOpenLuo(
                new AppConfig
                {
                    DatabasePath = Path.Combine(_baseDir, "game.db"),
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
                _baseDir);

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IGameSessionRuntime>());
        Assert.NotNull(provider.GetRequiredService<IGameSessionCatalog>());
    }

    [Fact]
    public async Task GameSessionCatalog_OpenSession_UsesIGameSessionSurface()
    {
        var services = new ServiceCollection()
            .AddSingleton<IGameStreams>(_ => Substitute.For<IGameStreams>())
            .AddOpenLuo(
                new AppConfig
                {
                    DatabasePath = Path.Combine(_baseDir, "catalog-session.db"),
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
                _baseDir);

        await using var provider = services.BuildServiceProvider();

        var pluginHost = provider.GetRequiredService<IPluginHost>();
        await pluginHost.LoadAllAsync(Path.Combine(_baseDir, "data", "plugins"));

        var catalog = provider.GetRequiredService<IGameSessionCatalog>();
        var session = await catalog.OpenSessionAsync(new SessionOpenRequest
        {
            ClientType = "test",
            ClientId = "catalog-client"
        });

        await session.InitGameAsync("school", "玩家");
        var state = await session.TryGetStateAsync();
        Assert.NotNull(state);

        var result = await session.SubmitAsync(new GameSessionInput
        {
            SourceId = "test",
            ChannelId = "main",
            ActorId = "player",
            Kind = SessionInputKind.Text,
            Text = "/characters"
        });

        Assert.NotEmpty(result.Events);
        Assert.Contains(result.Events, e => e is InputAcceptedEvent);
        Assert.NotEmpty(result.Events.OfType<TextOutputEvent>().Concat(result.Events.OfType<MessageEvent>().Cast<GameEvent>()));
    }

    [Fact]
    public async Task GameSessionRuntime_OpenAndSubmit_ProducesSessionAndOutputEvents()
    {
        var services = new ServiceCollection()
            .AddSingleton<IGameStreams>(_ => Substitute.For<IGameStreams>())
            .AddOpenLuo(
                new AppConfig
                {
                    DatabasePath = Path.Combine(_baseDir, "game2.db"),
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
                _baseDir);

        await using var provider = services.BuildServiceProvider();

        var pluginHost = provider.GetRequiredService<IPluginHost>();
        await pluginHost.LoadAllAsync(Path.Combine(_baseDir, "data", "plugins"));

        var runtime = provider.GetRequiredService<IGameSessionRuntime>();
        var session = await runtime.OpenAsync(new SessionOpenRequest
        {
            ClientType = "test",
            ClientId = "test-client"
        });

        await runtime.InitializeGameAsync(session.SessionId, "school", "玩家");
        var result = await runtime.SubmitAsync(new SessionInput
        {
            SessionId = session.SessionId,
            SourceId = "test",
            ChannelId = "main",
            ActorId = "player",
            Kind = SessionInputKind.Text,
            Text = "/characters"
        });

        Assert.NotEmpty(result.Events);
        Assert.Contains(result.Events, e => e is InputAcceptedEvent);
        Assert.NotEmpty(result.Events.OfType<TextOutputEvent>().Concat(result.Events.OfType<MessageEvent>().Cast<GameEvent>()));
        Assert.Contains(result.Events, e => e is StatusSnapshotEvent);
    }

    [Fact]
    public async Task GameSessionRuntime_StreamEventsAsync_YieldsPublishedEvents()
    {
        var services = new ServiceCollection()
            .AddSingleton<IGameStreams>(_ => Substitute.For<IGameStreams>())
            .AddOpenLuo(
                new AppConfig
                {
                    DatabasePath = Path.Combine(_baseDir, "game3.db"),
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
                _baseDir);

        await using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IGameSessionRuntime>();
        var session = await runtime.OpenAsync(new SessionOpenRequest
        {
            ClientType = "test",
            ClientId = "stream-client"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var streamTask = Task.Run(async () =>
        {
            await foreach (var @event in runtime.StreamEventsAsync(session.SessionId, cts.Token))
            {
                if (@event is SessionStateEvent or TextOutputEvent or MessageEvent)
                    return @event;
            }

            throw new InvalidOperationException("No event received.");
        }, cts.Token);

        await runtime.InitializeGameAsync(session.SessionId, "school", "玩家");
        await runtime.SubmitAsync(new SessionInput
        {
            SessionId = session.SessionId,
            SourceId = "test",
            ChannelId = "main",
            ActorId = "player",
            Kind = SessionInputKind.Text,
            Text = "/characters"
        });

        var firstEvent = await streamTask;
        Assert.True(firstEvent is SessionStateEvent or TextOutputEvent or MessageEvent);
    }

    [Fact]
    public async Task GameSessionRuntime_SubmitAsync_WithBinaryOnlyInput_ReturnsUnsupportedError()
    {
        var services = new ServiceCollection()
            .AddSingleton<IGameStreams>(_ => Substitute.For<IGameStreams>())
            .AddOpenLuo(
                new AppConfig
                {
                    DatabasePath = Path.Combine(_baseDir, "game4.db"),
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
                _baseDir);

        await using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IGameSessionRuntime>();
        var session = await runtime.OpenAsync(new SessionOpenRequest
        {
            ClientType = "test",
            ClientId = "binary-client"
        });

        var result = await runtime.SubmitAsync(new SessionInput
        {
            SessionId = session.SessionId,
            SourceId = "test",
            ChannelId = "main",
            ActorId = "player",
            Kind = SessionInputKind.Text,
            Parts =
            [
                new SessionInputPart
                {
                    Kind = SessionContentKind.Binary,
                    Name = "image.png",
                    MediaType = "image/png",
                    Data = [0x89, 0x50, 0x4E, 0x47]
                }
            ]
        });

        Assert.Contains(result.Events, e => e is ErrorEvent);
        Assert.Contains(result.Events, e => e is SystemNoticeEvent);
        Assert.Contains(result.Events, e => e is AttachmentAcceptedEvent);

        var attachments = await runtime.GetAttachmentsAsync(session.SessionId);
        var attachment = Assert.Single(attachments);
        Assert.Equal(SessionContentKind.Binary, attachment.Kind);
        Assert.Equal("image/png", attachment.MediaType);
        Assert.Null(attachment.AssetId);
    }

    [Fact]
    public async Task GameSessionRuntime_SubmitAsync_WithTextAndBinary_ExecutesTextAndEmitsNotice()
    {
        var services = new ServiceCollection()
            .AddSingleton<IGameStreams>(_ => Substitute.For<IGameStreams>())
            .AddOpenLuo(
                new AppConfig
                {
                    DatabasePath = Path.Combine(_baseDir, "game5.db"),
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
                _baseDir);

        await using var provider = services.BuildServiceProvider();

        var pluginHost = provider.GetRequiredService<IPluginHost>();
        await pluginHost.LoadAllAsync(Path.Combine(_baseDir, "data", "plugins"));

        var runtime = provider.GetRequiredService<IGameSessionRuntime>();
        var session = await runtime.OpenAsync(new SessionOpenRequest
        {
            ClientType = "test",
            ClientId = "mixed-client"
        });

        await runtime.InitializeGameAsync(session.SessionId, "school", "玩家");
        var result = await runtime.SubmitAsync(new SessionInput
        {
            SessionId = session.SessionId,
            SourceId = "test",
            ChannelId = "main",
            ActorId = "player",
            Kind = SessionInputKind.Text,
            Parts =
            [
                new SessionInputPart
                {
                    Kind = SessionContentKind.Text,
                    Text = "/characters"
                },
                new SessionInputPart
                {
                    Kind = SessionContentKind.Binary,
                    Name = "preview.jpg",
                    MediaType = "image/jpeg",
                    Data = [0xFF, 0xD8, 0xFF]
                }
            ]
        });

        Assert.Contains(result.Events, e => e is SystemNoticeEvent);
        Assert.Contains(result.Events, e => e is AttachmentAcceptedEvent);
        Assert.NotEmpty(result.Events.OfType<TextOutputEvent>().Concat(result.Events.OfType<MessageEvent>().Cast<GameEvent>()));
        Assert.Contains(result.Events, e => e is InputAcceptedEvent accepted && accepted.Attachments.Count == 1);
        var accepted = Assert.Single(result.Events.OfType<AttachmentAcceptedEvent>());
        Assert.False(string.IsNullOrWhiteSpace(accepted.AssetId));
        var attachments = await runtime.GetAttachmentsAsync(session.SessionId);
        Assert.Contains(attachments, a => a.AssetId == accepted.AssetId);

        var descriptor = await runtime.GetAssetDescriptorAsync(session.SessionId, accepted.AssetId!);
        Assert.NotNull(descriptor);
        Assert.Equal(accepted.AssetId, descriptor!.AssetId);

        var blob = await runtime.GetAssetBlobAsync(session.SessionId, accepted.AssetId!);
        Assert.NotNull(blob);
        Assert.Equal("image/jpeg", blob!.MimeType);
        Assert.Equal([0xFF, 0xD8, 0xFF], blob.Data);
    }

    [Fact]
    public async Task GameSessionRuntime_SubmitAsync_WithFileReference_StoresAttachmentPayload()
    {
        var services = new ServiceCollection()
            .AddSingleton<IGameStreams>(_ => Substitute.For<IGameStreams>())
            .AddOpenLuo(
                new AppConfig
                {
                    DatabasePath = Path.Combine(_baseDir, "game6.db"),
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
                _baseDir);

        var filePath = Path.Combine(_baseDir, "sample.bin");
        await File.WriteAllBytesAsync(filePath, [0x01, 0x02, 0x03, 0x04]);

        await using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IGameSessionRuntime>();
        var session = await runtime.OpenAsync(new SessionOpenRequest
        {
            ClientType = "test",
            ClientId = "file-ref-client"
        });

        var result = await runtime.SubmitAsync(new SessionInput
        {
            SessionId = session.SessionId,
            SourceId = "test",
            ChannelId = "main",
            ActorId = "player",
            Kind = SessionInputKind.Text,
            Parts =
            [
                new SessionInputPart
                {
                    Kind = SessionContentKind.FileReference,
                    Name = "sample.bin",
                    MediaType = "application/octet-stream",
                    FilePath = filePath
                }
            ]
        });

        var accepted = Assert.Single(result.Events.OfType<AttachmentAcceptedEvent>());
        var payload = await runtime.GetAttachmentAsync(session.SessionId, accepted.AttachmentId);
        Assert.NotNull(payload);
        Assert.Equal(4, payload!.Data.Length);
        Assert.Equal(filePath, payload.Attachment.OriginalFilePath);
        Assert.Null(payload.Attachment.AssetId);
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
