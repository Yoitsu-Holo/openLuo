using openLuo.Infrastructure.Database;
using openLuo.Modules.Embedding.Core.Interfaces;
using openLuo.Modules.Memory.Application;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;
using openLuo.Modules.Memory.Infrastructure.Retrieval;
using openLuo.Modules.Memory.Infrastructure.Storage;

namespace openLuo.playgraound.Demos.Executor;

internal static class MemoryKeywordFallbackDemo
{
    public static async Task<int> RunAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"openluo-memory-fallback-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            var initializer = new DatabaseInitializer(connectionString);
            await initializer.InitializeAsync();

            var embeddingClient = new DisabledEmbeddingClient();
            var connectionFactory = new SqliteConnectionFactory(connectionString);
            var repository = new SqliteMemoryRepository(connectionFactory);
            var writeService = new MemoryCommitCoordinator(
                new DefaultMemoryWriteProjector(),
                repository,
                embeddingClient);
            IMemoryRecallService recallService = new MemoryRecallCoordinator(
                new CompositeMemoryRetriever(
                    new VectorMemoryRetriever(connectionFactory, embeddingClient),
                    new KeywordMemoryRetriever(connectionFactory)));

            await SeedAsync(writeService);

            Console.WriteLine("=== Memory Keyword Fallback Demo ===");
            Console.WriteLine("backend: MemoryRecallCoordinator -> CompositeMemoryRetriever");
            Console.WriteLine("embeddingEnabled: false");
            Console.WriteLine("mode: keyword + fuzzy fallback");
            Console.WriteLine();

            await RunQueryAsync(recallService, "我回来了，衣服有点湿。");
            await RunQueryAsync(recallService, "昨天淋雨以后差点着凉。");
            await RunQueryAsync(recallService, "前几天熬夜之后你提醒我要先喝点热茶。");

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

    private static async Task SeedAsync(IMemoryWriteService writeService)
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
                Importance = 0.6f,
                Emotion = MemoryEmotion.Positive,
                OccurredAtUtc = DateTime.UtcNow.AddDays(-3)
            },
            new MemoryWriteInput
            {
                GameId = gameId,
                CharacterId = characterId,
                RawContent = "前几天主人熬夜后精神很差，汐泠让主人先喝点热茶，再慢慢说话。",
                Importance = 0.4f,
                Emotion = MemoryEmotion.Positive,
                OccurredAtUtc = DateTime.UtcNow.AddDays(-7)
            },
            new MemoryWriteInput
            {
                GameId = gameId,
                CharacterId = characterId,
                RawContent = "某次夜里起风，汐泠担心主人受寒，提前准备了外套和热水。",
                Importance = 0.3f,
                Emotion = MemoryEmotion.Positive,
                OccurredAtUtc = DateTime.UtcNow.AddDays(-10)
            }
        };

        foreach (var memory in memories)
            await writeService.WriteAsync(memory, CancellationToken.None);
    }

    private static async Task RunQueryAsync(IMemoryRecallService recallService, string query)
    {
        Console.WriteLine($"query: {query}");
        var result = await recallService.RecallAsync(new SemanticRecallQuery
        {
            GameId = "playground-demo",
            CharacterId = "rin",
            SearchText = query,
            TopK = 3,
            Scopes = [MemoryScope.CharacterPrivate]
        }, CancellationToken.None);

        if (result.Records.Count == 0)
        {
            Console.WriteLine("- <no match>");
            Console.WriteLine();
            return;
        }

        foreach (var memory in result.Records)
        {
            Console.WriteLine($"- {memory.Id} | importance={memory.Importance:F1} | summary={memory.Summary}");
        }

        Console.WriteLine();
    }

    private sealed class DisabledEmbeddingClient : IEmbeddingClient
    {
        public bool Enabled => false;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            throw new InvalidOperationException("Embedding is disabled in this demo.");
    }

}
