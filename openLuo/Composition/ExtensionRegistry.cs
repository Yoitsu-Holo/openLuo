using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace openLuo.Composition;

/// <summary>
/// 扩展注册表：ExtensionHost.ScanAndLoad() 之后由宿主填充；
/// 能力目录/调度器/上下文组装器按需从中读取（延迟解析，扩展先加载后构造）。
/// </summary>
public sealed class ExtensionRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private IReadOnlyList<LoadedExtension> _extensions = [];

    public ExtensionRegistry(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public void SetExtensions(IReadOnlyList<LoadedExtension> extensions) => _extensions = extensions;

    public IReadOnlyList<CapabilityDescriptor> Capabilities =>
        _extensions.SelectMany(e => e.Capabilities).ToList();

    public IReadOnlyDictionary<string, ICapabilityInvoker> Invokers =>
        _extensions
            .SelectMany(e => e.InvokableCapabilities)
            .GroupBy(pair => pair.Descriptor.CanonicalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Invoker, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IContextContributor> Contributors
    {
        get
        {
            var result = new List<IContextContributor>();
            foreach (var registration in _extensions.SelectMany(e => e.ContextContributors))
            {
                if (registration.Instance is not null)
                {
                    result.Add(registration.Instance);
                }
                else
                {
                    var instance = ActivatorUtilities.CreateInstance(_serviceProvider, registration.ContributorType);
                    if (instance is IContextContributor contributor)
                        result.Add(contributor);
                }
            }
            return result;
        }
    }

    public IReadOnlyList<IMessageTagRenderer> TagRenderers =>
        _extensions.SelectMany(e => e.TagRenderers).ToList();

    public IReadOnlyList<IStateMutationHandler> StateHandlers =>
        _extensions.SelectMany(e => e.StateHandlers).Select(h => h.Handler).ToList();
}
