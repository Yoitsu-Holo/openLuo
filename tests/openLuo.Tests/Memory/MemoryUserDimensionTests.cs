using Microsoft.Data.Sqlite;
using openLuo.Infrastructure.Database;
using openLuo.Modules.Memory.Core.Models;
using openLuo.Modules.Memory.Infrastructure.Retrieval;
using openLuo.Modules.Memory.Infrastructure.Storage;

namespace openLuo.Tests.Memory;

/// <summary>
/// 记忆的「用户维度」（D45）：<c>memories.user_id</c> + 召回过滤。
/// 语义约定（三条都要守住）：
/// <list type="number">
/// <item>带 userId 写入 → 只有同一用户能召回（隔离）；</item>
/// <item><c>user_id = ''</c> 的记忆 = 未分区，对所有用户可见（老数据/平台没给 userId 时写的东西）；</item>
/// <item>查询不给 userId → 完全不过滤，行为与引入该维度之前一致。</item>
/// </list>
/// </summary>
public sealed class MemoryUserDimensionTests : IDisposable
{
    private const string GameId = "session-1";
    private const string CharacterId = "builtin-rin";
    private const string UserA = "10001";
    private const string UserB = "10002";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "openluo-memuser-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task 带用户写入_召回按用户隔离_未分区记忆对所有用户可见()
    {
        var dbPath = CreateDbPath();
        await InitializeSchemaAsync(dbPath);
        var repository = new SqliteMemoryRepository(Factory(dbPath));
        var retriever = new KeywordMemoryRetriever(Factory(dbPath));

        await repository.StoreRecordAsync(Record("m-a", UserA, "樱花祭的约定"));
        await repository.StoreRecordAsync(Record("m-b", UserB, "樱花祭的约定"));
        await repository.StoreRecordAsync(Record("m-shared", userId: string.Empty, "樱花祭的共同约定"));

        // 落库确实按用户分了区（列真的有值，而不是一律空串）
        Assert.Equal(new[] { string.Empty, UserA, UserB }, (await QueryUserIdsAsync(dbPath)).Order(StringComparer.Ordinal).ToArray());

        // 用户 A：拿到自己的 + 未分区的；**拿不到** B 的私有记忆（隔离断言）
        var forA = SortedIds(await RecallAsync(retriever, UserA));
        Assert.Equal(new[] { "m-a", "m-shared" }, forA);
        Assert.DoesNotContain("m-b", forA);

        // 用户 B 对称成立
        var forB = SortedIds(await RecallAsync(retriever, UserB));
        Assert.Equal(new[] { "m-b", "m-shared" }, forB);
        Assert.DoesNotContain("m-a", forB);

        // 低层存储视图同样按用户过滤（QueryRecentAsync 的 userId 参数）
        var recentForA = SortedIds(await repository.QueryRecentAsync(
            GameId, CharacterId, [MemoryScope.CharacterPrivate], limit: 10, userId: UserA));
        Assert.Equal(new[] { "m-a", "m-shared" }, recentForA);
    }

    [Fact]
    public async Task 查询不带用户_召回不过滤_与引入用户维度前一致()
    {
        var dbPath = CreateDbPath();
        await InitializeSchemaAsync(dbPath);
        var repository = new SqliteMemoryRepository(Factory(dbPath));
        var retriever = new KeywordMemoryRetriever(Factory(dbPath));

        await repository.StoreRecordAsync(Record("m-a", UserA, "樱花祭的约定"));
        await repository.StoreRecordAsync(Record("m-b", UserB, "樱花祭的约定"));
        await repository.StoreRecordAsync(Record("m-shared", userId: string.Empty, "樱花祭的共同约定"));

        // UserId = null（老调用方 / 平台没给 userId）→ 看不到"用户过滤"这件事，三个用户分区的记忆都在
        var unfiltered = SortedIds(await RecallAsync(retriever, userId: null));
        Assert.Equal(new[] { "m-a", "m-b", "m-shared" }, unfiltered);

        // 低层存储视图在 userId 省略时同样不过滤（默认参数 = 老行为）
        var recent = SortedIds(await repository.QueryRecentAsync(
            GameId, CharacterId, [MemoryScope.CharacterPrivate], limit: 10));
        Assert.Equal(new[] { "m-a", "m-b", "m-shared" }, recent);
    }

    [Fact]
    public async Task 老库无user_id列_初始化后自动补列且可读写()
    {
        var dbPath = CreateDbPath();
        await CreateLegacyDatabaseAsync(dbPath);

        // 前置：这是"老库"——列不存在，索引也不存在
        Assert.DoesNotContain("user_id", await GetColumnsAsync(dbPath));
        Assert.False(await IndexExistsAsync(dbPath, "idx_memories_game_character_user"));

        await InitializeSchemaAsync(dbPath);

        Assert.Contains("user_id", await GetColumnsAsync(dbPath));
        Assert.True(await IndexExistsAsync(dbPath, "idx_memories_game_character_user"));

        // 幂等：同一个库再初始化一次不报错、状态不变
        await InitializeSchemaAsync(dbPath);
        Assert.Contains("user_id", await GetColumnsAsync(dbPath));

        var repository = new SqliteMemoryRepository(Factory(dbPath));
        var retriever = new KeywordMemoryRetriever(Factory(dbPath));

        // 老行（补列时的默认值）读回来是空串 = 未分区，绝不是某个用户的私有记忆
        var legacy = await repository.QueryRecentAsync(GameId, CharacterId, [MemoryScope.CharacterPrivate], limit: 10);
        Assert.Equal(new[] { "m-legacy" }, legacy.Select(r => r.Id).ToArray());
        Assert.Equal(string.Empty, legacy[0].UserId);

        // 补出来的列可读可写：新写入带 userId 的记忆按用户隔离
        await repository.StoreRecordAsync(Record("m-a", UserA, "樱花祭的约定"));

        Assert.Equal(new[] { "m-a" }, SortedIds(await RecallAsync(retriever, UserA)));
        // 用户 B 搜同样的词：匹配不到 A 的私有记忆，回退分支只给出未分区的老行
        var forB = SortedIds(await RecallAsync(retriever, UserB));
        Assert.DoesNotContain("m-a", forB);
        Assert.Equal(new[] { "m-legacy" }, forB);
    }

    [Fact]
    public async Task 极老库整表重建后仍带user_id列_补列不被重建抹掉()
    {
        var dbPath = CreateDbPath();
        await CreatePreV2DatabaseAsync(dbPath);

        // 前置：这种表缺 memory_format_version → 走"整表重建"路径（DROP + RENAME），而不是加法补列
        Assert.DoesNotContain("memory_format_version", await GetColumnsAsync(dbPath));

        await InitializeSchemaAsync(dbPath);

        // 重建必须先于补列：否则 memories_new 里没有 user_id，补出来的列会被重建静默抹掉
        Assert.Contains("user_id", await GetColumnsAsync(dbPath));
        Assert.Contains("memory_format_version", await GetColumnsAsync(dbPath));
        Assert.True(await IndexExistsAsync(dbPath, "idx_memories_game_character_user"));

        // 老数据被原样搬过来，并落到"未分区"
        var repository = new SqliteMemoryRepository(Factory(dbPath));
        var legacy = await repository.QueryRecentAsync(GameId, CharacterId, [MemoryScope.CharacterPrivate], limit: 10);
        Assert.Equal(new[] { "m-legacy" }, legacy.Select(r => r.Id).ToArray());
        Assert.Equal(string.Empty, legacy[0].UserId);
    }

    private string CreateDbPath()
    {
        Directory.CreateDirectory(_dir);
        return Path.Combine(_dir, "hub.db");
    }

    private static SqliteConnectionFactory Factory(string dbPath) => new($"Data Source={dbPath}");

    private static Task InitializeSchemaAsync(string dbPath) =>
        // 走真实初始化路径（建表 + 守卫式迁移）；baseDir 留空，原生库路径显式传入。
        new DatabaseInitializer($"Data Source={dbPath}", sqliteVecExtensionPath: ResolveSqliteVecExtensionPath())
            .InitializeAsync();

    private static MemoryRecord Record(string id, string? userId, string text) => new()
    {
        Id = id,
        GameId = GameId,
        OwnerCharacterId = CharacterId,
        UserId = userId ?? string.Empty,
        OccurredAtUtc = DateTime.UtcNow,
        SourceText = text,
        Summary = text,
        RecallText = text,
        Importance = 0.5f,
        Salience = 0.5f
    };

    private static async Task<IReadOnlyList<MemoryRecord>> RecallAsync(KeywordMemoryRetriever retriever, string? userId)
    {
        var result = await retriever.RetrieveAsync(new SemanticRecallQuery
        {
            GameId = GameId,
            CharacterId = CharacterId,
            UserId = userId,
            SearchText = "樱花祭的约定",
            TopK = 5,
            Scopes = [MemoryScope.CharacterPrivate, MemoryScope.Shared]
        });

        Assert.True(result.Success, "关键字检索器不应降级/失败");
        return result.Records;
    }

    private static string[] SortedIds(IEnumerable<MemoryRecord> records) =>
        records.Select(r => r.Id).Order(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// 造一个"引入 user_id 之前"的库：建表语句与当时逐列一致（EnsureMemoriesTableSchemaAsync 只在缺列时才整表重建，
    /// 所以这种表会走"加法补列"而不是重建路径），并塞一条老记忆。
    /// </summary>
    private static async Task CreateLegacyDatabaseAsync(string dbPath)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE memories (
                id TEXT PRIMARY KEY,
                game_id TEXT NOT NULL DEFAULT '',
                character_id TEXT NOT NULL,
                memory_format_version INTEGER NOT NULL DEFAULT 2,
                scope INTEGER NOT NULL DEFAULT 0,
                source_text TEXT NOT NULL DEFAULT '',
                summary TEXT NOT NULL DEFAULT '',
                recall_text TEXT NOT NULL DEFAULT '',
                tags_json TEXT NOT NULL DEFAULT '[]',
                entities_json TEXT NOT NULL DEFAULT '[]',
                metadata_json TEXT NOT NULL DEFAULT '{}',
                emotion INTEGER NOT NULL DEFAULT 0,
                importance REAL NOT NULL DEFAULT 0.5,
                salience REAL NOT NULL DEFAULT 0.5,
                emotional_weight INTEGER DEFAULT 0,
                occurred_at TEXT NOT NULL,
                is_compressed INTEGER DEFAULT 0
            );
            INSERT INTO memories (id, game_id, character_id, occurred_at, source_text, summary, recall_text)
            VALUES ('m-legacy', 'session-1', 'builtin-rin', '2026-01-01T00:00:00.0000000Z', '旧版记忆', '旧版记忆', '旧版记忆');
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 造一个更老的库：表里还有 <c>content</c>（v2 之前）且没有 <c>memory_format_version</c>，
    /// 于是初始化会走"整表重建"分支——补列必须排在它之后才不会被抹掉。
    /// </summary>
    private static async Task CreatePreV2DatabaseAsync(string dbPath)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE memories (
                id TEXT PRIMARY KEY,
                game_id TEXT NOT NULL DEFAULT '',
                character_id TEXT NOT NULL,
                content TEXT NOT NULL DEFAULT '',
                scope INTEGER DEFAULT 0,
                source_text TEXT DEFAULT '',
                summary TEXT DEFAULT '',
                recall_text TEXT DEFAULT '',
                tags_json TEXT DEFAULT '[]',
                entities_json TEXT DEFAULT '[]',
                metadata_json TEXT DEFAULT '{}',
                emotion INTEGER,
                importance REAL,
                salience REAL,
                emotional_weight INTEGER DEFAULT 0,
                occurred_at TEXT NOT NULL,
                is_compressed INTEGER DEFAULT 0
            );
            INSERT INTO memories (id, game_id, character_id, content, occurred_at)
            VALUES ('m-legacy', 'session-1', 'builtin-rin', '旧版记忆', '2026-01-01T00:00:00.0000000Z');
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> GetColumnsAsync(string dbPath)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM pragma_table_info('memories')";
        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));
        return columns;
    }

    private static async Task<List<string>> QueryUserIdsAsync(string dbPath)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT user_id FROM memories";
        var userIds = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            userIds.Add(reader.IsDBNull(0) ? "<null>" : reader.GetString(0));
        return userIds;
    }

    private static async Task<bool> IndexExistsAsync(string dbPath, string indexName)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = @name";
        cmd.Parameters.AddWithValue("@name", indexName);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static string ResolveSqliteVecExtensionPath()
    {
        // DatabaseInitializer.InitializeAsync 会顺带创建 vec_memories（需要 sqlite-vec 原生库）。
        // 原生库是 Content 项、不随 ProjectReference 复制，故从源码树里找宿主输出同款的那一份。
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var nativeRoot = Path.Combine(dir.FullName, "openLuo", "native", "sqlite-vec");
            if (!Directory.Exists(nativeRoot))
                continue;

            var library = Directory.EnumerateFiles(nativeRoot, "vec0.*", SearchOption.AllDirectories).FirstOrDefault();
            if (library is not null)
                return library;
        }

        throw new InvalidOperationException(
            $"未找到 sqlite-vec 原生库（从 {AppContext.BaseDirectory} 向上查找 openLuo/native/sqlite-vec）。");
    }
}
