using openLuo.Modules.GameBridge.Infrastructure.Handlers;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public interface ISessionGameApiFactory
{
    ISessionGameApi Create(SessionHandle handle);
}

internal sealed class SessionGameApiFactory(
    ISessionRegistry sessionRegistry,
    GameStateApiHandler stateHandler,
    PlayerApiHandler playerHandler,
    ShopApiHandler shopHandler,
    GiftApiHandler giftHandler,
    ResourceApiHandler resourceHandler,
    AssetApiHandler assetHandler,
    LifecycleApiHandler lifecycleHandler) : ISessionGameApiFactory
{
    public ISessionGameApi Create(SessionHandle handle) =>
        new SessionScopedGameApi(
            handle, sessionRegistry,
            stateHandler, playerHandler, shopHandler, giftHandler,
            resourceHandler, assetHandler, lifecycleHandler);
}
