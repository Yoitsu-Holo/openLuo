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
        await RebuildMemoriesFtsAsync(conn);
        await InitializeSqliteVecAsync(conn);
    }

    private async Task InitializeSqliteVecAsync(SqliteConnection conn)
    {
        if (sqliteVecDimensions <= 0)
            throw new InvalidOperationException(
                $"config.sqliteVec.vectorDimensions 必须是正整数，当前值: {sqliteVecDimensions}");

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
                "检测到已存在 vec_memories 表，但无法解析 embedding 维度。请确认该表由 sqlite-vec 创建并包含 embedding float[N] 列。"
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
                        COALESCE(NULLIF(m.game_id, ''), (SELECT id FROM game_state LIMIT 1), ''),
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
        // v2: add current_minute column
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE game_state ADD COLUMN current_minute INTEGER DEFAULT 480";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* column already exists */ }

        // v3: add current_location column
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE game_state ADD COLUMN current_location TEXT DEFAULT ''";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* column already exists */ }

        // v5: add last_interaction_day column
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE game_state ADD COLUMN last_interaction_day INTEGER DEFAULT 1";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* column already exists */ }


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

        // Backfill empty game_id with current game_state id
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE memories
                SET game_id = COALESCE(
                    NULLIF((SELECT id FROM game_state LIMIT 1), ''),
                    game_id
                )
                WHERE IFNULL(game_id, '') = ''
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // v7: add game_id column to diaries for multi-save isolation
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE diaries ADD COLUMN game_id TEXT DEFAULT ''";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* column already exists */ }

        // Backfill empty diary game_id with the latest known game_state id.
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE diaries
                SET game_id = COALESCE(
                    NULLIF((SELECT id FROM game_state ORDER BY updated_at DESC, created_at DESC, id ASC LIMIT 1), ''),
                    game_id
                )
                WHERE IFNULL(game_id, '') = ''
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await EnsureInventoryScopedByGameAsync(conn);
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

    private static async Task RebuildMemoriesFtsAsync(SqliteConnection conn)
    {
        await using (var dropInsertTrigger = conn.CreateCommand())
        {
            dropInsertTrigger.CommandText = "DROP TRIGGER IF EXISTS memories_fts_insert";
            await dropInsertTrigger.ExecuteNonQueryAsync();
        }

        await using (var dropDeleteTrigger = conn.CreateCommand())
        {
            dropDeleteTrigger.CommandText = "DROP TRIGGER IF EXISTS memories_fts_delete";
            await dropDeleteTrigger.ExecuteNonQueryAsync();
        }

        await using (var dropUpdateTrigger = conn.CreateCommand())
        {
            dropUpdateTrigger.CommandText = "DROP TRIGGER IF EXISTS memories_fts_update";
            await dropUpdateTrigger.ExecuteNonQueryAsync();
        }

        await using (var dropFts = conn.CreateCommand())
        {
            dropFts.CommandText = "DROP TABLE IF EXISTS memories_fts";
            await dropFts.ExecuteNonQueryAsync();
        }

        await using (var createFts = conn.CreateCommand())
        {
            createFts.CommandText = """
                CREATE VIRTUAL TABLE memories_fts USING fts5(
                    summary,
                    recall_text,
                    source_text,
                    tokenize='unicode61'
                )
                """;
            await createFts.ExecuteNonQueryAsync();
        }

        await using (var seedFts = conn.CreateCommand())
        {
            seedFts.CommandText = """
                INSERT INTO memories_fts(rowid, summary, recall_text, source_text)
                SELECT rowid, summary, recall_text, source_text
                FROM memories
                """;
            await seedFts.ExecuteNonQueryAsync();
        }

        await using (var createInsertTrigger = conn.CreateCommand())
        {
            createInsertTrigger.CommandText = """
                CREATE TRIGGER memories_fts_insert AFTER INSERT ON memories BEGIN
                    INSERT INTO memories_fts(rowid, summary, recall_text, source_text)
                    VALUES (new.rowid, new.summary, new.recall_text, new.source_text);
                END
                """;
            await createInsertTrigger.ExecuteNonQueryAsync();
        }

        await using (var createDeleteTrigger = conn.CreateCommand())
        {
            createDeleteTrigger.CommandText = """
                CREATE TRIGGER memories_fts_delete AFTER DELETE ON memories BEGIN
                    INSERT INTO memories_fts(memories_fts, rowid, summary, recall_text, source_text)
                    VALUES ('delete', old.rowid, old.summary, old.recall_text, old.source_text);
                END
                """;
            await createDeleteTrigger.ExecuteNonQueryAsync();
        }

        await using (var createUpdateTrigger = conn.CreateCommand())
        {
            createUpdateTrigger.CommandText = """
                CREATE TRIGGER memories_fts_update AFTER UPDATE ON memories BEGIN
                    INSERT INTO memories_fts(memories_fts, rowid, summary, recall_text, source_text)
                    VALUES ('delete', old.rowid, old.summary, old.recall_text, old.source_text);
                    INSERT INTO memories_fts(rowid, summary, recall_text, source_text)
                    VALUES (new.rowid, new.summary, new.recall_text, new.source_text);
                END
                """;
            await createUpdateTrigger.ExecuteNonQueryAsync();
        }
    }

    private static async Task CreateTablesAsync(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS game_state (
                id TEXT PRIMARY KEY,
                player_name TEXT NOT NULL,
                archetype_id TEXT NOT NULL,
                active_character_id TEXT DEFAULT '',
                current_location TEXT DEFAULT '',
                current_day INTEGER DEFAULT 1,
                current_minute INTEGER DEFAULT 480,
                last_interaction_day INTEGER DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS characters (
                id TEXT PRIMARY KEY,
                game_id TEXT NOT NULL DEFAULT '',
                archetype_id TEXT NOT NULL,
                name TEXT NOT NULL,
                display_priority INTEGER NOT NULL DEFAULT 100,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                role_profile_json TEXT NOT NULL DEFAULT '{}',
                agent_policy_json TEXT NOT NULL DEFAULT '{}',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS affection_events (
                id TEXT PRIMARY KEY,
                character_id TEXT NOT NULL,
                reason TEXT NOT NULL,
                delta INTEGER NOT NULL,
                occurred_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS memories (
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

            CREATE TABLE IF NOT EXISTS inventory (
                game_id TEXT NOT NULL DEFAULT '',
                item_id TEXT NOT NULL,
                quantity INTEGER DEFAULT 0,
                PRIMARY KEY (game_id, item_id)
            );

            CREATE TABLE IF NOT EXISTS shop_offers (
                game_id TEXT NOT NULL,
                category_id TEXT NOT NULL,
                item_id TEXT NOT NULL,
                price INTEGER NOT NULL,
                sort_order INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (game_id, category_id, item_id)
            );

            CREATE TABLE IF NOT EXISTS diaries (
                id          TEXT PRIMARY KEY,
                day         INTEGER NOT NULL,
                content     TEXT NOT NULL,
                created_at  TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS agent_state (
                character_id   TEXT NOT NULL PRIMARY KEY,
                intentions     TEXT NOT NULL DEFAULT '[]',
                avoid_topics   TEXT NOT NULL DEFAULT '[]',
                next_milestone TEXT,
                planned_at     TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS timeline_events (
                id               TEXT PRIMARY KEY,
                game_id          TEXT NOT NULL,
                event_type       TEXT NOT NULL,
                title            TEXT NOT NULL,
                due_at_epoch_ms  INTEGER NOT NULL,
                end_at_epoch_ms  INTEGER,
                recurrence_rule  TEXT,
                status           TEXT NOT NULL DEFAULT 'pending',
                action_json      TEXT,
                context_json     TEXT,
                created_at       TEXT NOT NULL,
                updated_at       TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS character_schedules (
                id              TEXT PRIMARY KEY,
                game_id         TEXT NOT NULL,
                character_id    TEXT NOT NULL,
                active_days     TEXT NOT NULL DEFAULT '1,2,3,4,5,6,7',
                start_minute    INTEGER NOT NULL DEFAULT 0,
                end_minute      INTEGER NOT NULL DEFAULT 1439,
                allow_parallel  INTEGER NOT NULL DEFAULT 1,
                priority        INTEGER NOT NULL DEFAULT 100,
                created_at      TEXT NOT NULL,
                updated_at      TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS party_tasks (
                id              TEXT PRIMARY KEY,
                game_id         TEXT NOT NULL,
                title           TEXT NOT NULL,
                requested_by    TEXT NOT NULL,
                status          TEXT NOT NULL DEFAULT 'pending',
                context_json    TEXT NOT NULL DEFAULT '{}',
                created_at      TEXT NOT NULL,
                updated_at      TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS party_task_steps (
                id                    TEXT PRIMARY KEY,
                task_id               TEXT NOT NULL,
                step_order            INTEGER NOT NULL,
                assigned_character_id TEXT NOT NULL,
                role                  TEXT NOT NULL DEFAULT 'support',
                instruction           TEXT NOT NULL,
                result_json           TEXT NOT NULL DEFAULT '{}',
                status                TEXT NOT NULL DEFAULT 'pending',
                started_at            TEXT,
                finished_at           TEXT,
                created_at            TEXT NOT NULL,
                updated_at            TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS channel_bindings (
                id                  TEXT PRIMARY KEY,
                game_id             TEXT NOT NULL,
                platform            TEXT NOT NULL,
                channel_or_user_id  TEXT NOT NULL,
                character_id        TEXT NOT NULL,
                is_default          INTEGER NOT NULL DEFAULT 0,
                created_at          TEXT NOT NULL,
                updated_at          TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS agent_contexts (
                game_id       TEXT NOT NULL,
                character_id  TEXT NOT NULL,
                summary       TEXT NOT NULL DEFAULT '',
                created_at    TEXT NOT NULL,
                updated_at    TEXT NOT NULL,
                PRIMARY KEY (game_id, character_id)
            );

            CREATE TABLE IF NOT EXISTS agent_context_turns (
                id            TEXT PRIMARY KEY,
                game_id       TEXT NOT NULL,
                character_id  TEXT NOT NULL,
                speaker_id    TEXT NOT NULL,
                speaker_role  TEXT NOT NULL,
                content       TEXT NOT NULL,
                timestamp_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_memories_character
                ON memories(character_id, occurred_at DESC);
            CREATE INDEX IF NOT EXISTS idx_memories_game_character
                ON memories(game_id, character_id, occurred_at DESC);
            CREATE INDEX IF NOT EXISTS idx_shop_offers_category
                ON shop_offers(game_id, category_id, sort_order);
            CREATE INDEX IF NOT EXISTS idx_affection_events_character
                ON affection_events(character_id, occurred_at DESC);
            CREATE INDEX IF NOT EXISTS idx_characters_game
                ON characters(game_id, is_enabled, display_priority);
            CREATE INDEX IF NOT EXISTS idx_characters_archetype
                ON characters(archetype_id, updated_at DESC);
            CREATE INDEX IF NOT EXISTS idx_timeline_events_due
                ON timeline_events(game_id, status, due_at_epoch_ms);
            CREATE INDEX IF NOT EXISTS idx_timeline_events_type
                ON timeline_events(game_id, event_type, status, due_at_epoch_ms);
            CREATE INDEX IF NOT EXISTS idx_character_schedules_game
                ON character_schedules(game_id, character_id, priority);
            CREATE INDEX IF NOT EXISTS idx_party_tasks_game
                ON party_tasks(game_id, status, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_party_task_steps_task
                ON party_task_steps(task_id, step_order);
            CREATE INDEX IF NOT EXISTS idx_channel_bindings_game
                ON channel_bindings(game_id, platform, is_default DESC);
            CREATE INDEX IF NOT EXISTS idx_agent_context_turns_lookup
                ON agent_context_turns(game_id, character_id, timestamp_utc ASC);

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

            -- Asset System Tables
            CREATE TABLE IF NOT EXISTS asset_defs (
                id            TEXT PRIMARY KEY,
                asset_type    TEXT NOT NULL,
                namespace     TEXT NOT NULL,
                mime_family   TEXT,
                plugin_id     TEXT,
                metadata_json TEXT,
                created_at    TEXT NOT NULL,
                updated_at    TEXT NOT NULL,
                UNIQUE(asset_type, namespace)
            );

            CREATE TABLE IF NOT EXISTS asset_records (
                id                TEXT PRIMARY KEY,
                game_id           TEXT NOT NULL,
                asset_type        TEXT NOT NULL,
                namespace         TEXT NOT NULL,
                owner_kind        TEXT NOT NULL,
                owner_id          TEXT NOT NULL,
                label             TEXT,
                status            TEXT NOT NULL DEFAULT 'active',
                source_type       TEXT NOT NULL DEFAULT 'manual',
                primary_mime_type TEXT,
                created_at        TEXT NOT NULL,
                updated_at        TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS asset_blob_payloads (
                id          TEXT PRIMARY KEY,
                asset_id    TEXT NOT NULL,
                mime_type   TEXT NOT NULL,
                blob_role   TEXT NOT NULL DEFAULT 'primary',
                blob_data   BLOB NOT NULL,
                size_bytes  INTEGER NOT NULL,
                sha256      TEXT,
                is_primary  INTEGER NOT NULL DEFAULT 1,
                created_at  TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS asset_meta_json (
                id           TEXT PRIMARY KEY,
                asset_id     TEXT NOT NULL,
                meta_type    TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                created_at   TEXT NOT NULL
            );

            -- Relation System Tables
            CREATE TABLE IF NOT EXISTS entity_links (
                id                TEXT PRIMARY KEY,
                game_id           TEXT NOT NULL,
                from_entity_type  TEXT NOT NULL,
                from_entity_id    TEXT NOT NULL,
                to_entity_type    TEXT NOT NULL,
                to_entity_id      TEXT NOT NULL,
                link_type         TEXT NOT NULL,
                metadata_json     TEXT,
                created_at        TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS unlock_records (
                id            TEXT PRIMARY KEY,
                game_id       TEXT NOT NULL,
                owner_kind    TEXT NOT NULL,
                owner_id      TEXT NOT NULL,
                entity_type   TEXT NOT NULL,
                entity_id     TEXT NOT NULL,
                unlock_type   TEXT NOT NULL,
                metadata_json TEXT,
                unlocked_at   TEXT NOT NULL
            );

            -- Indexes for new tables
            CREATE INDEX IF NOT EXISTS idx_state_values_lookup
                ON state_values(game_id, owner_kind, owner_id);
            CREATE INDEX IF NOT EXISTS idx_state_values_namespace
                ON state_values(game_id, namespace);
            CREATE INDEX IF NOT EXISTS idx_state_change_logs_owner
                ON state_change_logs(game_id, owner_kind, owner_id);
            CREATE INDEX IF NOT EXISTS idx_asset_records_type
                ON asset_records(game_id, asset_type);
            CREATE INDEX IF NOT EXISTS idx_asset_records_owner
                ON asset_records(game_id, owner_kind, owner_id);
            CREATE INDEX IF NOT EXISTS idx_entity_links_from
                ON entity_links(game_id, from_entity_type, from_entity_id);
            CREATE INDEX IF NOT EXISTS idx_entity_links_to
                ON entity_links(game_id, to_entity_type, to_entity_id);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureInventoryScopedByGameAsync(SqliteConnection conn)
    {
        var inventorySql = await GetTableSqlAsync(conn, "inventory");
        if (string.IsNullOrWhiteSpace(inventorySql))
            return;

        if (ContainsColumn(inventorySql!, "game_id"))
        {
            await using var createIndex = conn.CreateCommand();
            createIndex.CommandText = "CREATE INDEX IF NOT EXISTS idx_inventory_game ON inventory(game_id)";
            await createIndex.ExecuteNonQueryAsync();
            return;
        }

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();

        await using (var create = conn.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = """
                CREATE TABLE inventory_new (
                    game_id TEXT NOT NULL DEFAULT '',
                    item_id TEXT NOT NULL,
                    quantity INTEGER DEFAULT 0,
                    PRIMARY KEY (game_id, item_id)
                )
                """;
            await create.ExecuteNonQueryAsync();
        }

        await using (var migrate = conn.CreateCommand())
        {
            migrate.Transaction = tx;
            migrate.CommandText = """
                INSERT INTO inventory_new (game_id, item_id, quantity)
                SELECT
                    COALESCE(NULLIF((SELECT id FROM game_state LIMIT 1), ''), ''),
                    item_id,
                    quantity
                FROM inventory
                """;
            await migrate.ExecuteNonQueryAsync();
        }

        await using (var dropOld = conn.CreateCommand())
        {
            dropOld.Transaction = tx;
            dropOld.CommandText = "DROP TABLE inventory";
            await dropOld.ExecuteNonQueryAsync();
        }

        await using (var rename = conn.CreateCommand())
        {
            rename.Transaction = tx;
            rename.CommandText = "ALTER TABLE inventory_new RENAME TO inventory";
            await rename.ExecuteNonQueryAsync();
        }

        await using (var createIndex = conn.CreateCommand())
        {
            createIndex.Transaction = tx;
            createIndex.CommandText = "CREATE INDEX IF NOT EXISTS idx_inventory_game ON inventory(game_id)";
            await createIndex.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }
}
