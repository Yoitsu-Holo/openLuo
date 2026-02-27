using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Modules.PluginRuntime.Core.Interfaces;

public interface IGameBridgeContextAccessor
{
    GameBridgeRequestContext? Current { get; set; }
}
