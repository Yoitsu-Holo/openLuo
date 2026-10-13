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

    /// <summary>扩展声明的技能（id 已被命名空间化为 <c>&lt;ext&gt;:&lt;id&gt;</c>）。</summary>
    public IReadOnlyList<SkillDocument> Skills => _extensions.SelectMany(e => e.Skills).ToList();

    /// <summary>扩展声明的工作流（同样是黑盒能力，模型只按 id 触发）。</summary>
    public IReadOnlyList<WorkflowDefinition> Workflows => _extensions.SelectMany(e => e.Workflows).ToList();

    /// <summary>工作流节点处理器（按 HandlerId 分发）。</summary>
    public IReadOnlyList<IWorkflowNodeHandler> WorkflowHandlers =>
        _extensions.SelectMany(e => e.WorkflowNodeHandlers).ToList();

    public IReadOnlyList<IMessageTagRenderer> TagRenderers =>
        _extensions.SelectMany(e => e.TagRenderers).ToList();

    public IReadOnlyList<IStateMutationHandler> StateHandlers =>
        _extensions.SelectMany(e => e.StateHandlers).Select(h => h.Handler).ToList();
}
