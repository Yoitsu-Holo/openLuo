using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;
using openLuo.Modules.Assets.Infrastructure;

namespace openLuo.Modules.GameBridge.Infrastructure.Handlers;

public class AssetApiHandler(
    IGameStateRepository stateRepo,
    IAssetRegistry assetRegistry,
    IAssetStore assetStore,
    IAssetBlobStore blobStore,
    IAssetMetaStore metaStore,
    IAssetLinkService linkService,
    IAssetQueryService queryService,
    IAssetUnlockService unlockService,
    IGameLogger logger,
    IGameBridgeContextAccessor bridgeContextAccessor,
    AssetDefStore? defStore = null)
{
    private static readonly JsonSerializerOptions _serOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── game/asset/register ────────────────────────────────────────────────────

    [GameApi("game/asset/register")]
    public JsonNode? RegisterAssetDef(string assetType, string @namespace, string? mimeFamily = null, string? pluginId = null, JsonNode? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(assetType) || string.IsNullOrWhiteSpace(@namespace))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"missing def\"}");

        var metadataJson = metadata is not null
            ? JsonSerializer.Serialize(metadata, _serOpts)
            : null;

        var assetDef = new AssetDef
        {
            AssetType    = assetType,
            Namespace    = @namespace,
            MimeFamily   = mimeFamily,
            PluginId     = pluginId,
            MetadataJson = metadataJson
        };

        assetRegistry.Register(assetDef);
        defStore?.Upsert(assetDef);
        logger?.Info("asset/register", $"registered {assetDef.DefinitionId}");

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok           = true,
            definitionId = assetDef.DefinitionId
        }, _serOpts));
    }

    // ── game/asset/create ──────────────────────────────────────────────────────

    [GameApi("game/asset/create")]
    public async Task<JsonNode?> CreateGameAssetAsync(string gameId, string assetType, string @namespace, string ownerKind = "game", string ownerId = "global", string? label = null, string sourceType = "manual")
    {
        var game = await ResolveStateAsync(gameId);
        if (game is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        var newId = $"asset_{Guid.NewGuid():N}";
        var now   = DateTime.UtcNow.ToString("O");

        var record = new AssetRecord
        {
            Id         = newId,
            GameId     = game.Id,
            AssetType  = assetType,
            Namespace  = @namespace,
            OwnerKind  = ownerKind,
            OwnerId    = ownerId,
            Label      = label,
            SourceType = sourceType,
            Status     = "active",
            CreatedAt  = now,
            UpdatedAt  = now
        };

        await assetStore.CreateAsync(record);

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok      = true,
            assetId = newId
        }, _serOpts));
    }

    // ── game/asset/get ─────────────────────────────────────────────────────────

    [GameApi("game/asset/get")]
    public async Task<JsonNode?> GetGameAssetAsync(string assetId, bool includeBlobInfo = true, bool includeMeta = true, bool includeLinks = false)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"missing assetId\"}");

        var record = await assetStore.GetByIdAsync(assetId);
        if (record is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"not_found\"}");

        List<AssetBlobPayload> blobs = includeBlobInfo
            ? await blobStore.GetInfoByAssetIdAsync(assetId)
            : [];

        List<AssetMetaJson> metas = includeMeta
            ? await metaStore.GetByAssetIdAsync(assetId)
            : [];

        List<EntityLink> links = includeLinks
            ? await linkService.GetLinksFromAsync("asset", assetId)
            : [];

        var blobDtos = blobs.Select(b => new
        {
            blobId   = b.Id,
            mimeType = b.MimeType,
            blobRole = b.BlobRole,
            isPrimary = b.IsPrimary,
            sizeBytes = b.SizeBytes,
            sha256   = b.Sha256
        }).ToList();

        var metaDtos = metas.Select(m => new
        {
            metaId  = m.Id,
            metaType = m.MetaType,
            payload = JsonNode.Parse(m.PayloadJson)
        }).ToList();

        var linkDtos = links.Select(l => new
        {
            linkId       = l.Id,
            linkType     = l.LinkType,
            toEntityType = l.ToEntityType,
            toEntityId   = l.ToEntityId
        }).ToList();

        var asset = new
        {
            assetId    = record.Id,
            gameId     = record.GameId,
            assetType  = record.AssetType,
            @namespace = record.Namespace,
            ownerKind  = record.OwnerKind,
            ownerId    = record.OwnerId,
            label      = record.Label,
            status     = record.Status,
            sourceType = record.SourceType,
            createdAt  = record.CreatedAt,
            updatedAt  = record.UpdatedAt
        };

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok        = true,
            asset,
            blobInfos = blobDtos,
            meta      = metaDtos,
            links     = linkDtos
        }, _serOpts));
    }

    // ── game/asset/query ───────────────────────────────────────────────────────

    [GameApi("game/asset/query")]
    public async Task<JsonNode?> QueryGameAssetsAsync(string? gameId = null, string? assetType = null, string? @namespace = null, string? ownerKind = null, string? ownerId = null, string? sourceType = null, string? labelLike = null, string? linkedToEntityType = null, string? linkedToEntityId = null, int limit = 50, int offset = 0)
    {
        var game = await ResolveStateAsync(gameId);
        if (game is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        var records = await queryService.QueryAssetsAsync(
            game.Id,
            assetType,
            @namespace,
            ownerKind,
            ownerId,
            sourceType,
            labelLike,
            linkedToEntityType,
            linkedToEntityId,
            limit,
            offset);

        var items = records.Select(r => new
        {
            assetId    = r.Id,
            assetType  = r.AssetType,
            @namespace = r.Namespace,
            ownerKind  = r.OwnerKind,
            ownerId    = r.OwnerId,
            label      = r.Label,
            sourceType = r.SourceType,
            status     = r.Status,
            createdAt  = r.CreatedAt
        }).ToList();

        return JsonNode.Parse(JsonSerializer.Serialize(new { ok = true, items }, _serOpts));
    }

    // ── game/asset/blob_put ────────────────────────────────────────────────────

    [GameApi("game/asset/blob_put")]
    public async Task<JsonNode?> PutAssetBlobAsync(string assetId, string mimeType, string blobRole, string blobBase64, bool isPrimary = false)
    {
        byte[] data = Convert.FromBase64String(blobBase64);

        var blobId = await blobStore.PutAsync(assetId, mimeType, blobRole, data, isPrimary);

        var allBlobs  = await blobStore.GetInfoByAssetIdAsync(assetId);
        var blobInfo  = allBlobs.FirstOrDefault(b => b.Id == blobId);

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok        = true,
            blobId,
            sizeBytes = blobInfo?.SizeBytes ?? 0L,
            sha256    = blobInfo?.Sha256
        }, _serOpts));
    }

    // ── game/asset/meta_put ────────────────────────────────────────────────────

    [GameApi("game/asset/meta_put")]
    public async Task<JsonNode?> PutAssetMetaAsync(string assetId, string metaType, JsonNode? payload)
    {
        var payloadJson = payload is not null
            ? JsonSerializer.Serialize(payload, _serOpts)
            : "{}";

        var metaId = await metaStore.PutAsync(assetId, metaType, payloadJson);

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok     = true,
            metaId
        }, _serOpts));
    }

    // ── game/asset/link ────────────────────────────────────────────────────────

    [GameApi("game/asset/link")]
    public async Task<JsonNode?> LinkGameAssetAsync(string gameId, string fromEntityType = "asset", string fromEntityId = "", string toEntityType = "", string toEntityId = "", string linkType = "", JsonNode? metadata = null)
    {
        var game = await ResolveStateAsync(gameId);
        if (game is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        var metadataJson = metadata is not null
            ? JsonSerializer.Serialize(metadata, _serOpts)
            : null;

        var linkId = await linkService.CreateLinkAsync(
            game.Id,
            fromEntityType,
            fromEntityId,
            toEntityType,
            toEntityId,
            linkType,
            metadataJson);

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok     = true,
            linkId
        }, _serOpts));
    }

    // ── game/asset/unlock ──────────────────────────────────────────────────────

    [GameApi("game/asset/unlock")]
    public async Task<JsonNode?> UnlockGameAssetAsync(string gameId, string ownerKind = "game", string ownerId = "gallery_main", string entityType = "asset", string entityId = "", string unlockType = "gallery_unlock", JsonNode? metadata = null)
    {
        var game = await ResolveStateAsync(gameId);
        if (game is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        var metadataJson = metadata is not null
            ? JsonSerializer.Serialize(metadata, _serOpts)
            : null;

        var unlockId = await unlockService.UnlockAsync(
            game.Id,
            ownerKind,
            ownerId,
            entityType,
            entityId,
            unlockType,
            metadataJson);

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok       = true,
            unlockId
        }, _serOpts));
    }

    private async Task<GameState?> ResolveStateAsync(string? gameId)
    {
        if (!string.IsNullOrWhiteSpace(gameId))
            return await stateRepo.GetAsync(gameId);

        var contextGameId = bridgeContextAccessor.Current?.GameId;
        if (!string.IsNullOrWhiteSpace(contextGameId))
            return await stateRepo.GetAsync(contextGameId);

        return null;
    }
}
