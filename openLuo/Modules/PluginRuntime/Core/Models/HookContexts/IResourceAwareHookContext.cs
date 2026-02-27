using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.PluginRuntime.Core.Models.HookContexts;

public interface IResourceAwareHookContext
{
    IReadOnlyList<ResourceStatusItemView> ResourceSnapshot { get; set; }
}
