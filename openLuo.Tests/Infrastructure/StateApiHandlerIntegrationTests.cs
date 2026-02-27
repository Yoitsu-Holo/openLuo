using System.Text.Json.Nodes;
using Dapper;
using openLuo.Modules.AppShell.Application;
using openLuo.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;
using openLuo.Infrastructure.Runtime;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Infrastructure.Database;
using openLuo.Infrastructure.Logging;
using openLuo.Modules.GameBridge.Infrastructure.Handlers;
using openLuo.Modules.WorldState.Infrastructure.State;
using Microsoft.Data.Sqlite;
using NSubstitute;
using Xunit;

namespace openLuo.Infrastructure.Tests;

public sealed class StateApiHandlerIntegrationTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly string _connStr;
    private readonly IGameStateRepository _stateRepo = Substitute.For<IGameStateRepository>();
    private readonly IGameBridgeContextAccessor _bridgeContextAccessor = Substitute.For<IGameBridgeContextAccessor>();
    private readonly IGameContextAccessor _gameContextAccessor = Substitute.For<IGameContextAccessor>();
    private StateApiHandler _handler = null!;

    public StateApiHandlerIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gimai_state_{Guid.NewGuid():N}.db");
        _connStr = $"Data Source={_dbPath}";
    }

    public async Task InitializeAsync()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();

        var store = new StateStore(_connStr);
        var defStore = new StateDefStore(_connStr);
        var registry = new StateRegistry(defStore);
        var logger = new GameLogger(Path.GetTempPath(), new openLuo.Modules.AppShell.Application.LogConfig());

        _handler = new StateApiHandler(
            _stateRepo,
            registry,
            new StateMutationService(registry, store),
            new StateQueryService(registry, store),
            logger,
            _bridgeContextAccessor,
            defStore);

        var state = new GameState
        {
            Id = "g1",
            PlayerName = "Player",
            ArchetypeId = "bg1",
            CurrentDay = 1,
            CurrentMinute = 480,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _stateRepo.GetAsync(default).Returns(state);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };
        _bridgeContextAccessor.Current.Returns(new GameBridgeRequestContext { GameId = "g1" });
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Register_PersistsDefinition_AndCanRehydrateRegistry()
    {
        var result = _handler.RegisterStateDef("affection", "char_status", "number", ownerKind: "character", defaultValue: "0", min: "0", max: "1000", mutableByLlm: true, derived: false, statusGroup: "intimacy", statusOrder: 100, hiddenFromStatus: false, displayFormat: "好感：{value}/1000", promptContext: "好感度决定关系阶段", pluginId: "builtin_char_status_affection", metadata: JsonNode.Parse("""{"unit":"point"}"""));

        Assert.True(result!["ok"]!.GetValue<bool>());

        await using var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();
        var row = await conn.QuerySingleAsync<(string namespaceValue, string keyValue, string ownerKind, string metadataJson)>(
            """
            SELECT namespace AS namespaceValue, key AS keyValue, owner_kind AS ownerKind, metadata_json AS metadataJson
            FROM state_defs
            WHERE id = @id
            """,
            new { id = "char_status:character:affection" });

        Assert.Equal("char_status", row.namespaceValue);
        Assert.Equal("affection", row.keyValue);
        Assert.Equal("character", row.ownerKind);
        Assert.Contains("\"unit\":\"point\"", row.metadataJson);

        var rehydrated = new StateRegistry(new StateDefStore(_connStr));
        var def = rehydrated.GetDef("char_status", StateOwnerKind.Character, "affection");
        Assert.NotNull(def);
        Assert.Equal("builtin_char_status_affection", def!.PluginId);
        Assert.Equal("0", def.DefaultValue);
    }

    [Fact(Skip = "Legacy JSON format no longer applicable to typed RegisterStateDef API")]
    public Task Register_LegacyResourceShape_IsRejected()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Apply_AffectionMutation_AutoDerivesRelationshipStage()
    {
        _handler.RegisterStateDef("affection", "char_status", "number", ownerKind: "character", defaultValue: "0", min: "0", max: "1000", mutableByLlm: true, derived: false);
        _handler.RegisterStateDef("relationship_stage", "char_status", "enum", ownerKind: "character", defaultValue: "陌生人", mutableByLlm: false, derived: true, enumValues: JsonNode.Parse("""["陌生人","熟人","朋友","好友","恋人"]"""));

        var applyResult = await _handler.ApplyStateMutationsAsync("g1", JsonNode.Parse("""
            [{"namespace":"char_status","key":"affection","ownerKind":"character","ownerId":"c1","op":"set","value":"450"}]
            """));
        Assert.True(applyResult!["ok"]!.GetValue<bool>());
        Assert.True(applyResult["results"]![0]!["ok"]!.GetValue<bool>());

        var stageResult = await _handler.GetStateValueAsync("g1", "char_status", "relationship_stage", "character", "c1");
        Assert.True(stageResult!["ok"]!.GetValue<bool>());
        Assert.Equal("朋友", stageResult["value"]!.GetValue<string>());

        await using var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();
        var deriveLog = await conn.QuerySingleOrDefaultAsync<(string changeType, string sourceType, string sourceId)>(
            """
            SELECT change_type AS changeType, source_type AS sourceType, source_id AS sourceId
            FROM state_change_logs
            WHERE namespace='char_status'
              AND key='relationship_stage'
              AND owner_id='c1'
            ORDER BY created_at DESC
            LIMIT 1
            """);
        Assert.Equal("derive", deriveLog.changeType);
        Assert.Equal("state_derived", deriveLog.sourceType);
        Assert.Equal("char_status.affection", deriveLog.sourceId);
    }
}
