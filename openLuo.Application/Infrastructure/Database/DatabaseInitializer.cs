using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace openLuo.Infrastructure.Database;

public class DatabaseInitializer(
    string connectionString,
    string? baseDir = null,
    string sqliteVecExtensionPath = "",
    int sqliteVecDimensions = 1536)
{
    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await CreateTablesAsync(conn);
        await MigrateAsync(conn);
        await EnsureMemoriesTableSchemaAsync(conn);
        await EnsureMemoryUserDimensionAsync(conn);
        await InitializeSqliteVecAsync(conn);
    }

    private async Task InitializeSqliteVecAsync(SqliteConnection conn)
    {
        if (sqliteVecDimensions <= 0)
            throw new InvalidOperationException(
                $"config.sqliteVec.vectorDimensions must be a positive integer; current value: {sqliteVecDimensions}");

        var resolvedBaseDir = string.IsNullOrWhiteSpace(baseDir) ? AppContext.BaseDirectory : baseDir;
        var extensionPath = SqliteVecExtensionLoader.ResolveExtensionPath(resolvedBaseDir, sqliteVecExtensionPath);
        SqliteVecExtensionLoader.Load(conn, extensionPath);

        await EnsureVecTableAsync(conn, sqliteVecDimensions);
    }

    private static async Task EnsureVecTableAsync(SqliteConnection conn, int configuredDimensions)
    {
        var existingSql = await GetTableSqlAsync(conn, "vec_memories");

        if (string.IsNullOrWhiteSpace(existingSql))
        {
            await CreateVecTableAsync(conn, "vec_memories", configuredDimensions);
            return;
        }

        var existingDimensions = ParseEmbeddingDimensions(existingSql!);
        if (existingDimensions is null)
        {
            throw new InvalidOperationException(
                "vec_memories table already exists but the embedding dimension could not be resolved. Ensure the table was created by sqlite-vec with an embedding float[N] column."
            );
        }

        var hasGameId = ContainsColumn(existingSql!, "game_id");
        var hasCharacterId = ContainsColumn(existingSql!, "character_id");
        var schemaMatches = existingDimensions.Value == configuredDimensions && hasGameId && hasCharacterId;
        if (schemaMatches) return;

        await RebuildVecTableAsync(conn, configuredDimensions, hasGameId && hasCharacterId);
    }

    private static bool ContainsColumn(string schemaSql, string columnName) =>
        Regex.IsMatch(schemaSql, $@"\b{Regex.Escape(columnName)}\b", RegexOptions.IgnoreCase);

    private static async Task CreateVecTableAsync(
        SqliteConnection conn,
        string tableName,
        int dimensions,
        SqliteTransaction? tx = null)
    {
        await using var create = conn.CreateCommand();
        create.Transaction = tx;
        create.CommandText = $"""
            CREATE VIRTUAL TABLE {tableName} USING vec0(
                memory_id TEXT PRIMARY KEY,
                game_id TEXT,
                character_id TEXT,
                embedding FLOAT[{dimensions}]
            )
            """;
        await create.ExecuteNonQueryAsync();
    }

    private static async Task RebuildVecTableAsync(
        SqliteConnection conn, int configuredDimensions, bool sourceHasScopeColumns)
    {
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();

        await using (var dropTemp = conn.CreateCommand())
        {
            dropTemp.Transaction = tx;
            dropTemp.CommandText = "DROP TABLE IF EXISTS vec_memories_new";
            await dropTemp.ExecuteNonQueryAsync();
        }

        await CreateVecTableAsync(conn, "vec_memories_new", configuredDimensions, tx);

        await using (var migrate = conn.CreateCommand())
        {
            migrate.Transaction = tx;
            migrate.CommandText = sourceHasScopeColumns
                ? """
                    INSERT INTO vec_memories_new (memory_id, game_id, character_id, embedding)
                    SELECT memory_id, game_id, character_id, embedding
                    FROM vec_memories
                    """
                : """
                    INSERT INTO vec_memories_new (memory_id, game_id, character_id, embedding)
                    SELECT
                        v.memory_id,
                        COALESCE(m.game_id, ''),
                        m.character_id,
                        v.embedding
                    FROM vec_memories v
                    JOIN memories m ON m.id = v.memory_id
                    """;
            await migrate.ExecuteNonQueryAsync();
        }

        await using (var dropOld = conn.CreateCommand())
        {
            dropOld.Transaction = tx;
            dropOld.CommandText = "DROP TABLE vec_memories";
            await dropOld.ExecuteNonQueryAsync();
        }

        await using (var rename = conn.CreateCommand())
        {
            rename.Transaction = tx;
            rename.CommandText = "ALTER TABLE vec_memories_new RENAME TO vec_memories";
            await rename.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    private static int? ParseEmbeddingDimensions(string schemaSql)
    {
        var match = Regex.Match(schemaSql, @"\bembedding\s+float\[(\d+)\]", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    private static async Task<string?> GetTableSqlAsync(SqliteConnection conn, string tableName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @name";
        cmd.Parameters.AddWithValue("@name", tableName);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private static async Task MigrateAsync(SqliteConnection conn)
    {
        // v6: add enum_values column to state_defs
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE state_defs ADD COLUMN enum_values TEXT DEFAULT '[]'";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* column already exists or table not created yet */ }

        // v8: add resource lifecycle columns to state_defs
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE state_defs ADD COLUMN lifecycle_state TEXT DEFAULT 'active'";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* column already exists or table not created yet */ }

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE state_defs ADD COLUMN retirement_policy TEXT DEFAULT 'keep_value'";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* column already exists or table not created yet */ }

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE state_defs ADD COLUMN source_kind TEXT";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* column already exists or table not created yet */ }

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE state_defs ADD COLUMN source_ref TEXT";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* column already exists or table not created yet */ }

        // v6: add game_id column to memories (for future multi-save isolation)
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE memories ADD COLUMN game_id TEXT DEFAULT ''";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* column already exists */ }

        // 不再回填 game_id：旧回填依赖已废弃的单存档游戏状态表（该表不再创建，子查询会直接报错），
        // 且多存档下应由写入方显式提供 game_id；空串不影响 memories 的读写。
    }

    private static async Task EnsureMemoriesTableSchemaAsync(SqliteConnection conn)
    {
        var schemaSql = await GetTableSqlAsync(conn, "memories");
        if (string.IsNullOrWhiteSpace(schemaSql))
            return;

        var requiresRebuild =
            ContainsColumn(schemaSql!, "content") ||
            !ContainsColumn(schemaSql!, "memory_format_version") ||
            !ContainsColumn(schemaSql!, "scope") ||
            !ContainsColumn(schemaSql!, "source_text") ||
            !ContainsColumn(schemaSql!, "summary") ||
            !ContainsColumn(schemaSql!, "recall_text") ||
            !ContainsColumn(schemaSql!, "tags_json") ||
            !ContainsColumn(schemaSql!, "entities_json") ||
            !ContainsColumn(schemaSql!, "metadata_json") ||
            !ContainsColumn(schemaSql!, "emotion") ||
            !ContainsColumn(schemaSql!, "importance") ||
            !ContainsColumn(schemaSql!, "salience");

        if (!requiresRebuild)
            return;

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();

        await using (var dropTemp = conn.CreateCommand())
        {
            dropTemp.Transaction = tx;
            dropTemp.CommandText = "DROP TABLE IF EXISTS memories_new";
            await dropTemp.ExecuteNonQueryAsync();
        }

        await using (var create = conn.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = """
                CREATE TABLE memories_new (
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
                )
                """;
            await create.ExecuteNonQueryAsync();
        }

        var hasContent = ContainsColumn(schemaSql!, "content");
        await using (var migrate = conn.CreateCommand())
        {
            migrate.Transaction = tx;
            migrate.CommandText = hasContent
                ? """
                    INSERT INTO memories_new (
                        id, game_id, character_id, memory_format_version, scope, source_text, summary, recall_text,
                        tags_json, entities_json, metadata_json, emotion, importance, salience, emotional_weight,
                        occurred_at, is_compressed)
                    SELECT
                        id,
                        game_id,
                        character_id,
                        2,
                        COALESCE(scope, 0),
                        CASE
                            WHEN IFNULL(source_text, '') <> '' THEN source_text
                            ELSE IFNULL(content, '')
                        END,
                        CASE
                            WHEN IFNULL(summary, '') <> '' THEN summary
                            ELSE IFNULL(content, '')
                        END,
                        CASE
                            WHEN IFNULL(recall_text, '') <> '' THEN recall_text
                            ELSE IFNULL(content, '')
                        END,
                        COALESCE(tags_json, '[]'),
                        COALESCE(entities_json, '[]'),
                        COALESCE(metadata_json, '{}'),
                        COALESCE(emotion, CASE
                            WHEN emotional_weight > 0 THEN 1
                            WHEN emotional_weight < 0 THEN 2
                            ELSE 0
                        END),
                        COALESCE(importance, MIN(1.0, ABS(emotional_weight) / 10.0)),
                        COALESCE(salience, 0.5),
                        emotional_weight,
                        occurred_at,
                        is_compressed
                    FROM memories
                    """
                : """
                    INSERT INTO memories_new (
                        id, game_id, character_id, memory_format_version, scope, source_text, summary, recall_text,
                        tags_json, entities_json, metadata_json, emotion, importance, salience, emotional_weight,
                        occurred_at, is_compressed)
                    SELECT
                        id,
                        game_id,
                        character_id,
                        COALESCE(memory_format_version, 2),
                        COALESCE(scope, 0),
                        source_text,
                        summary,
                        recall_text,
                        COALESCE(tags_json, '[]'),
                        COALESCE(entities_json, '[]'),
                        COALESCE(metadata_json, '{}'),
                        COALESCE(emotion, CASE
                            WHEN emotional_weight > 0 THEN 1
                            WHEN emotional_weight < 0 THEN 2
                            ELSE 0
                        END),
                        COALESCE(importance, MIN(1.0, ABS(emotional_weight) / 10.0)),
                        COALESCE(salience, 0.5),
                        emotional_weight,
                        occurred_at,
                        is_compressed
                    FROM memories
                    """;
            await migrate.ExecuteNonQueryAsync();
        }

        await using (var dropOld = conn.CreateCommand())
        {
            dropOld.Transaction = tx;
            dropOld.CommandText = "DROP TABLE memories";
            await dropOld.ExecuteNonQueryAsync();
        }

        await using (var rename = conn.CreateCommand())
        {
            rename.Transaction = tx;
            rename.CommandText = "ALTER TABLE memories_new RENAME TO memories";
            await rename.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    /// <summary>
    /// 给 <c>memories</c> 补"用户维度"（v9，D45）：列 <c>user_id</c> + 召回索引。
    /// 必须排在 <see cref="EnsureMemoriesTableSchemaAsync"/> **之后**：那条路径会 DROP/RENAME 重建表，
    /// 而它的 memories_new DDL 里没有 user_id（重建后由本方法补齐），先加列会被抹掉。
    /// 全程加法、幂等：老库新库与重建库都收敛到同一形状，老数据自动获得空串 = 不按用户分区。
    /// </summary>
    private static async Task EnsureMemoryUserDimensionAsync(SqliteConnection conn)
    {
        var tableSql = await GetTableSqlAsync(conn, "memories");
        if (string.IsNullOrWhiteSpace(tableSql))
            return;

        if (!ContainsColumn(tableSql!, "user_id"))
        {
            await using var alter = conn.CreateCommand();
            // NOT NULL DEFAULT '' 让已有行自动落到"未分区"，语义与引入用户维度前一致（召回时 user_id = '' 一律可见）。
            alter.CommandText = "ALTER TABLE memories ADD COLUMN user_id TEXT NOT NULL DEFAULT ''";
            await alter.ExecuteNonQueryAsync();
        }

        await using var index = conn.CreateCommand();
        // 召回 SQL 的形状是 (game_id, character_id) + user_id 过滤 + occurred_at 倒序，
        // 故复合索引的列序与它对齐（沿用 idx_memories_game_character 的写法）。
        index.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_memories_game_character_user
                ON memories(game_id, character_id, user_id, occurred_at DESC)
            """;
        await index.ExecuteNonQueryAsync();
    }

    private static async Task CreateTablesAsync(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            -- 只创建真正被读写的表：memories（SqliteMemoryRepository / KeywordMemoryRetriever）、
            -- state_defs / state_values / state_change_logs（WorldState 存储）。
            -- vec_memories 由 InitializeSqliteVecAsync 单独创建。
            -- 早先的单存档游戏状态 / 角色 / 背包与商店 / 日程与任务 / Agent 上下文 / 资产 / 关系等遗留表，
            -- 全仓已无任何读写点（既无 SQL 也无 Dapper 映射）；它们的原始消费者是**已被删除的旧插件宿主**
            -- （随之删除的 openLuo/data/plugins 里的 python 插件通过 plugin/* 的 call_host 访问这些表，
            -- 而该宿主在本仓库已无任何实现），因此不再创建；旧库中已存在的这些表不主动删除，避免破坏性迁移。
            CREATE TABLE IF NOT EXISTS memories (
                id TEXT PRIMARY KEY,
                game_id TEXT NOT NULL DEFAULT '',
                character_id TEXT NOT NULL,
                -- 用户维度（D45）：空串 = 平台未提供 userId，记忆不按用户分区（= 改造前语义）。
                -- 索引与老库补列都在 EnsureMemoryUserDimensionAsync（必须晚于整表重建）。
                user_id TEXT NOT NULL DEFAULT '',
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

            CREATE INDEX IF NOT EXISTS idx_memories_character
                ON memories(character_id, occurred_at DESC);
            CREATE INDEX IF NOT EXISTS idx_memories_game_character
                ON memories(game_id, character_id, occurred_at DESC);

            -- State System Tables
            CREATE TABLE IF NOT EXISTS state_defs (
                id                  TEXT PRIMARY KEY,
                namespace           TEXT NOT NULL,
                key                 TEXT NOT NULL,
                owner_kind          TEXT NOT NULL,
                value_type          TEXT NOT NULL,
                default_value       TEXT,
                min_value           TEXT,
                max_value           TEXT,
                derived             INTEGER NOT NULL DEFAULT 0,
                mutable_by_llm      INTEGER NOT NULL DEFAULT 0,
                status_group        TEXT,
                status_order        INTEGER NOT NULL DEFAULT 0,
                hidden_from_status  INTEGER NOT NULL DEFAULT 0,
                display_format      TEXT,
                prompt_context      TEXT,
                plugin_id           TEXT,
                metadata_json       TEXT,
                enum_values         TEXT NOT NULL DEFAULT '[]',
                lifecycle_state     TEXT NOT NULL DEFAULT 'active',
                retirement_policy   TEXT NOT NULL DEFAULT 'keep_value',
                source_kind         TEXT,
                source_ref          TEXT,
                created_at          TEXT NOT NULL,
                updated_at          TEXT NOT NULL,
                UNIQUE(namespace, key, owner_kind)
            );

            CREATE TABLE IF NOT EXISTS state_values (
                game_id     TEXT NOT NULL,
                owner_kind  TEXT NOT NULL,
                owner_id    TEXT NOT NULL,
                namespace   TEXT NOT NULL,
                key         TEXT NOT NULL,
                value_text  TEXT NOT NULL,
                updated_at  TEXT NOT NULL,
                PRIMARY KEY (game_id, owner_kind, owner_id, namespace, key)
            );

            CREATE TABLE IF NOT EXISTS state_change_logs (
                id          TEXT PRIMARY KEY,
                game_id     TEXT NOT NULL,
                owner_kind  TEXT NOT NULL,
                owner_id    TEXT NOT NULL,
                namespace   TEXT NOT NULL,
                key         TEXT NOT NULL,
                old_value   TEXT,
                new_value   TEXT NOT NULL,
                change_type TEXT NOT NULL,
                reason      TEXT,
                source_type TEXT,
                source_id   TEXT,
                created_at  TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_state_values_lookup
                ON state_values(game_id, owner_kind, owner_id);
            CREATE INDEX IF NOT EXISTS idx_state_values_namespace
                ON state_values(game_id, namespace);
            CREATE INDEX IF NOT EXISTS idx_state_change_logs_owner
                ON state_change_logs(game_id, owner_kind, owner_id);
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
