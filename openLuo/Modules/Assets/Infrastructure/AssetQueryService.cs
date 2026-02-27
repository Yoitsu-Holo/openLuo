using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;

namespace openLuo.Modules.Assets.Infrastructure;

public class AssetQueryService(IAssetStore assetStore, IAssetLinkService linkService) : IAssetQueryService
{
    // linkService is available for future cross-store queries if needed
    private readonly IAssetLinkService _linkService = linkService;

    public Task<List<AssetRecord>> QueryAssetsAsync(
        string gameId,
        string? assetType = null,
        string? @namespace = null,
        string? ownerKind = null,
        string? ownerId = null,
        string? sourceType = null,
        string? labelLike = null,
        string? linkedToEntityType = null,
        string? linkedToEntityId = null,
        int limit = 50,
        int offset = 0)
    {
        return assetStore.QueryAsync(
            gameId,
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
    }
}
