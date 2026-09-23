using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Composition;

/// <summary>把已加载扩展的能力暴露给能力目录（延迟读取注册表）。</summary>
public sealed class ExtensionCapabilitySource : ICapabilitySource
{
    private readonly ExtensionRegistry _registry;
    public ExtensionCapabilitySource(ExtensionRegistry registry) => _registry = registry;

    public string ProviderId => "extensions";

    public IReadOnlyList<CapabilityDescriptor> ListCapabilities() => _registry.Capabilities;
}
