using System.Threading;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Modules.PluginRuntime.Infrastructure;

public sealed class AsyncLocalGameBridgeContextAccessor : IGameBridgeContextAccessor
{
    private static readonly AsyncLocal<GameBridgeRequestContext?> CurrentContext = new();

    public GameBridgeRequestContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }
}
