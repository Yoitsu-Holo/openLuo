using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Data.Sqlite;
using openLuo.Modules.AppShell.Application;
using openLuo.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;
using openLuo.Infrastructure.Runtime;
using openLuo.Core.Models;
using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;
using openLuo.Modules.Assets.Infrastructure;
using openLuo.Infrastructure.Database;
using openLuo.Infrastructure.Logging;
using openLuo.Modules.GameBridge.Infrastructure.Handlers;
using NSubstitute;
using Xunit;

namespace openLuo.Infrastructure.Tests;

public sealed class AssetApiHandlerIntegrationTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly string _connStr;
    private readonly IGameStateRepository _stateRepo = Substitute.For<IGameStateRepository>();
    private readonly IGameBridgeContextAccessor _bridgeContextAccessor = Substitute.For<IGameBridgeContextAccessor>();
    private readonly IGameContextAccessor _gameContextAccessor = Substitute.For<IGameContextAccessor>();
    private AssetApiHandler _handler = null!;

    public AssetApiHandlerIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gimai_asset_{Guid.NewGuid():N}.db");
        _connStr = $"Data Source={_dbPath}";
    }

    public async Task InitializeAsync()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        var store      = new AssetStore(_connStr);
        var linkSvc    = new AssetLinkService(_connStr);
        var logger     = new GameLogger(Path.GetTempPath(), new LogConfig());

        _handler = new AssetApiHandler(
            _stateRepo,
            new AssetRegistry(),
            store,
            new AssetBlobStore(_connStr),
            new AssetMetaStore(_connStr),
            linkSvc,
            new AssetQueryService(store, linkSvc),
            new AssetUnlockService(_connStr),
            logger,
            _bridgeContextAccessor,
            new AssetDefStore(_connStr));

        var state = new GameState
        {
            Id          = "g1",
            PlayerName  = "Player",
            ArchetypeId = "bg1",
            CurrentDay  = 1,
            CurrentMinute = 480,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow
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

    // ── game/asset/register ───────────────────────────────────────────────────

    [Fact]
    public async Task Register_PersistsDefinitionToDatabase()
    {
        var result = _handler.RegisterAssetDef("dreamFragment", "gallery", mimeFamily: "text", pluginId: "example_dream_weaver", metadata: JsonNode.Parse("""{"supportsBlob":true}"""));

        Assert.True(result!["ok"]!.GetValue<bool>());

        await using var conn = new SqliteConnection(_connStr);
        await conn.OpenAsync();
        var row = await conn.QuerySingleAsync<(string id, string mime_family, string plugin_id, string metadata_json)>(
            "SELECT id, mime_family, plugin_id, metadata_json FROM asset_defs WHERE id = @id",
            new { id = "dreamFragment:gallery" });

        Assert.Equal("dreamFragment:gallery", row.id);
        Assert.Equal("text", row.mime_family);
        Assert.Equal("example_dream_weaver", row.plugin_id);
        Assert.Contains("supportsBlob", row.metadata_json);
    }

    [Fact]
    public void Register_ReturnsDefinitionId()
    {
        var result = _handler.RegisterAssetDef("dreamFragment", "gallery", pluginId: "example_dream_weaver");

        Assert.True(result!["ok"]!.GetValue<bool>());
        Assert.Equal("dreamFragment:gallery", result["definitionId"]!.GetValue<string>());
    }

    [Fact]
    public void Register_CanRehydratePersistedDefinition()
    {
        _handler.RegisterAssetDef("dreamFragment", "gallery", mimeFamily: "text", pluginId: "example_dream_weaver", metadata: JsonNode.Parse("""{"supportsBlob":true}"""));

        var registry = new AssetRegistry(new AssetDefStore(_connStr));
        var def = registry.GetDef("dreamFragment", "gallery");

        Assert.NotNull(def);
        Assert.Equal("text", def!.MimeFamily);
        Assert.Equal("example_dream_weaver", def.PluginId);
    }

    // ── game/asset/create ─────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsAssetId()
    {
        var result = await _handler.CreateGameAssetAsync("g1", "dreamFragment", "gallery", "character", "c1", "夜晚梦境", "ai");

        Assert.True(result!["ok"]!.GetValue<bool>());
        var id = result["assetId"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(id));
    }

    // ── Full pipeline: register → create → blob_put → meta_put → link → unlock → get ──

    [Fact]
    public async Task FullPipeline_AllStepsSucceed_GetReturnsCompleteRecord()
    {
        // Register
        _handler.RegisterAssetDef("dreamFragment", "gallery", pluginId: "example_dream_weaver");

        // Create
        var createRes = await _handler.CreateGameAssetAsync("g1", "dreamFragment", "gallery", "character", "c1", "樱花梦", "ai");
        Assert.True(createRes!["ok"]!.GetValue<bool>());
        var assetId = createRes["assetId"]!.GetValue<string>();

        // Blob put
        var textBytes  = System.Text.Encoding.UTF8.GetBytes("你梦见了一片樱花林，微风吹过，花瓣纷纷飘落……");
        var blobBase64 = Convert.ToBase64String(textBytes);
        var blobRes = await _handler.PutAssetBlobAsync(assetId, "text/plain", "content", blobBase64, true);
        Assert.True(blobRes!["ok"]!.GetValue<bool>());
        var blobId = blobRes["blobId"]!.GetValue<string>();
        Assert.True(blobRes["sizeBytes"]!.GetValue<long>() > 0);
        Assert.False(string.IsNullOrEmpty(blobRes["sha256"]!.GetValue<string>()));

        // Meta put
        var metaRes = await _handler.PutAssetMetaAsync(assetId, "dream_context", JsonNode.Parse("""{"affection":420,"day":3,"mood":"Happy"}"""));
        Assert.True(metaRes!["ok"]!.GetValue<bool>());
        Assert.False(string.IsNullOrEmpty(metaRes["metaId"]!.GetValue<string>()));

        // Link to character
        var linkRes = await _handler.LinkGameAssetAsync("g1", "asset", assetId, "character", "c1", "belongs_to");
        Assert.True(linkRes!["ok"]!.GetValue<bool>());

        // Unlock in gallery
        var unlockRes = await _handler.UnlockGameAssetAsync("g1", "game", "gallery_main", "asset", assetId, "dream_gallery");
        Assert.True(unlockRes!["ok"]!.GetValue<bool>());
        Assert.False(string.IsNullOrEmpty(unlockRes["unlockId"]!.GetValue<string>()));

        // Get — verify all attached data
        var getRes = await _handler.GetGameAssetAsync(assetId, true, true, true);
        Assert.True(getRes!["ok"]!.GetValue<bool>());
        Assert.Equal(assetId,   getRes["asset"]!["assetId"]!.GetValue<string>());
        Assert.Equal("樱花梦",   getRes["asset"]!["label"]!.GetValue<string>());
        Assert.Equal("ai",      getRes["asset"]!["sourceType"]!.GetValue<string>());

        var blobs = getRes["blobInfos"]!.AsArray();
        Assert.Single(blobs);
        Assert.Equal(blobId,    blobs[0]!["blobId"]!.GetValue<string>());
        Assert.True(blobs[0]!["isPrimary"]!.GetValue<bool>());

        var metas = getRes["meta"]!.AsArray();
        Assert.Single(metas);
        Assert.Equal("dream_context", metas[0]!["metaType"]!.GetValue<string>());
        Assert.Equal(420, metas[0]!["payload"]!["affection"]!.GetValue<int>());

        var links = getRes["links"]!.AsArray();
        Assert.Single(links);
        Assert.Equal("c1",          links[0]!["toEntityId"]!.GetValue<string>());
        Assert.Equal("belongs_to",  links[0]!["linkType"]!.GetValue<string>());
    }

    // ── game/asset/query ──────────────────────────────────────────────────────

    [Fact]
    public async Task Query_FiltersByAssetTypeAndOwner()
    {
        await _handler.CreateGameAssetAsync("g1", "dreamFragment", "gallery", "character", "c1", sourceType: "ai");
        await _handler.CreateGameAssetAsync("g1", "cg_scene", "gallery", "character", "c1", sourceType: "ai");
        await _handler.CreateGameAssetAsync("g1", "dreamFragment", "gallery", "character", "c2", sourceType: "ai");

        var result = await _handler.QueryGameAssetsAsync(assetType: "dreamFragment", ownerKind: "character", ownerId: "c1");

        Assert.True(result!["ok"]!.GetValue<bool>());
        var items = result["items"]!.AsArray();
        Assert.Single(items);
        Assert.Equal("dreamFragment", items[0]!["assetType"]!.GetValue<string>());
        Assert.Equal("c1",            items[0]!["ownerId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Query_LabelLike_FiltersCorrectly()
    {
        await _handler.CreateGameAssetAsync("g1", "dreamFragment", "gallery", "character", "c1", "夜晚梦境", "ai");
        await _handler.CreateGameAssetAsync("g1", "dreamFragment", "gallery", "character", "c1", "白日幻想", "ai");

        var result = await _handler.QueryGameAssetsAsync(assetType: "dreamFragment", labelLike: "梦境");

        Assert.True(result!["ok"]!.GetValue<bool>());
        Assert.Single(result["items"]!.AsArray());
        Assert.Equal("夜晚梦境", result["items"]![0]!["label"]!.GetValue<string>());
    }
}
