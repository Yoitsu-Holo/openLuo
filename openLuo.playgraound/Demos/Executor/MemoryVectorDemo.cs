using openLuo.Infrastructure.Database;
using openLuo.Modules.Memory.Application;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;
using openLuo.Modules.Memory.Infrastructure.Retrieval;
using openLuo.Modules.Memory.Infrastructure.Storage;
using openLuo.playgraound.Infrastructure;

namespace openLuo.playgraound.Demos.Executor;

internal static class MemoryVectorDemo
{
    public static async Task<int> RunAsync()
    {
        var embeddingClient = EmbeddingDemoBootstrap.TryCreateClient(out var settings, out var error);
        if (embeddingClient is null || settings is null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var dbPath = Path.Combine(Path.GetTempPath(), $"openluo-memory-vector-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            var initializer = new DatabaseInitializer(
                connectionString,
                AppContext.BaseDirectory,
                settings.SqliteVecExtensionPath,
                settings.VectorDimensions);
            await initializer.InitializeAsync();

            var sampleVector = await embeddingClient.EmbedAsync("memory-vector-demo dimension probe", CancellationToken.None);
            if (sampleVector.Length != settings.VectorDimensions)
            {
                Console.Error.WriteLine($"Configured sqliteVec.vectorDimensions={settings.VectorDimensions}, but embedding returned {sampleVector.Length}.");
                Console.Error.WriteLine($"Fix: edit {settings.ConfigPath} and set sqliteVec.vectorDimensions = {sampleVector.Length}");
                return 1;
            }

            var connectionFactory = new SqliteConnectionFactory(connectionString, AppContext.BaseDirectory, settings.SqliteVecExtensionPath);
            var repository = new SqliteMemoryRepository(connectionFactory);
            var writeService = new MemoryCommitCoordinator(
                new DefaultMemoryWriteProjector(),
                repository,
                embeddingClient,
                new ConsoleGameLogger());
            IMemoryRecallService recallService = new MemoryRecallCoordinator(
                new CompositeMemoryRetriever(
                    new VectorMemoryRetriever(connectionFactory, embeddingClient, new ConsoleGameLogger()),
                    new KeywordMemoryRetriever(connectionFactory)));

            await SeedAsync(writeService, settings);

            Console.WriteLine("=== Memory Vector Demo ===");
            Console.WriteLine($"provider: {settings.Embedding.Provider}");
            Console.WriteLine($"model: {settings.Embedding.Model}");
            Console.WriteLine($"vectorDimensions: {sampleVector.Length}");
            Console.WriteLine($"requestDelayMs: {settings.RequestDelayMs}");
            Console.WriteLine("pipeline: write -> embed -> sqlite-vec -> recall");
            Console.WriteLine();

            await RunQueryAsync(recallService, settings, "昨天淋雨后你给我拿了热茶。");
            await RunQueryAsync(recallService, settings, "前几天熬夜的时候你提醒我要休息。");
            await RunQueryAsync(recallService, settings, "晚上的风很冷，你提前给我准备外套。");

            return 0;
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
            catch
            {
                // ignore temp cleanup failure in playground demo
            }
        }
    }

    private static async Task SeedAsync(IMemoryWriteService writeService, EmbeddingDemoSettings settings)
    {
        var gameId = "playground-demo";
        var characterId = "rin";

        var memories = new[]
        {
            new MemoryWriteInput
            {
                GameId = gameId,
                CharacterId = characterId,
                RawContent = "主人上次淋雨回家后，衣服湿透了。汐泠立刻拿来干毛巾和热茶，提醒主人别着凉。",
                Importance = 0.8f,
                Emotion = MemoryEmotion.Positive,
                Source = "demo",
                OccurredAtUtc = DateTime.UtcNow.AddDays(-3)
            },
            new MemoryWriteInput
            {
                GameId = gameId,
                CharacterId = characterId,
                RawContent = "前几天主人熬夜后精神很差，汐泠让主人先喝点热茶，再慢慢说话。",
                Importance = 0.6f,
                Emotion = MemoryEmotion.Positive,
                Source = "demo",
                OccurredAtUtc = DateTime.UtcNow.AddDays(-7)
            },
            new MemoryWriteInput
            {
                GameId = gameId,
                CharacterId = characterId,
                RawContent = "某次夜里起风，汐泠担心主人受寒，提前准备了外套和热水。",
                Importance = 0.5f,
                Emotion = MemoryEmotion.Positive,
                Source = "demo",
                OccurredAtUtc = DateTime.UtcNow.AddDays(-10)
            }
        };

        foreach (var memory in memories)
        {
            var result = await writeService.WriteAsync(memory, CancellationToken.None);
            Console.WriteLine($"seed: {result.MemoryId} | trace={string.Join(", ", result.Trace)}");
            await DelayBetweenRequestsAsync(settings);
        }

        Console.WriteLine();
    }

    private static async Task RunQueryAsync(IMemoryRecallService recallService, EmbeddingDemoSettings settings, string query)
    {
        Console.WriteLine($"query: {query}");
        var result = await recallService.RecallAsync(new SemanticRecallQuery
        {
            GameId = "playground-demo",
            CharacterId = "rin",
            SearchText = query,
            TopK = 3,
            Scopes = [MemoryScope.CharacterPrivate],
            PreferImportant = true,
            PreferRecent = true,
            PreferEmotionallySalient = true
        }, CancellationToken.None);

        Console.WriteLine($"trace: {string.Join(" | ", result.Trace)}");

        if (result.Records.Count == 0)
        {
            Console.WriteLine("- <no match>");
            Console.WriteLine();
            return;
        }

        foreach (var memory in result.Records)
        {
            Console.WriteLine($"- {memory.Id} | salience={memory.Salience:F2} | summary={memory.Summary}");
        }

        Console.WriteLine();
        await DelayBetweenRequestsAsync(settings);
    }

    private static async Task DelayBetweenRequestsAsync(EmbeddingDemoSettings settings)
    {
        var delayMs = Math.Max(0, settings.RequestDelayMs);
        if (delayMs <= 0)
            return;

        Console.WriteLine($"wait: {delayMs}ms");
        await Task.Delay(delayMs);
    }
}
