using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Abstractions;

public interface IAgentExtension
{
    void Configure(ExtensionBuilder builder);
}

public sealed class ExtensionBuilder
{
    private readonly string _extensionId;
    private readonly List<CapabilityDescriptor> _capabilities = [];
    private readonly List<(CapabilityDescriptor Descriptor, ICapabilityInvoker Invoker)> _invokableCapabilities = [];
    private readonly List<ContextContributorRegistration> _contextContributors = [];
    private readonly List<WorkflowDefinition> _workflows = [];
    private readonly List<SkillDocument> _skills = [];
    private readonly List<IMessageTagRenderer> _tagRenderers = [];
    private readonly List<StateMutationHandlerRegistration> _stateHandlers = [];

    public ExtensionBuilder(string extensionId) => _extensionId = extensionId;
    public string ExtensionId => _extensionId;

    public ExtensionBuilder AddCapability(CapabilityDescriptor descriptor) => AddCapability(descriptor, NullInvoker.Instance);
    public ExtensionBuilder AddCapability(CapabilityDescriptor descriptor, ICapabilityInvoker invoker)
    {
        _invokableCapabilities.Add((descriptor, invoker));
        _capabilities.Add(descriptor);
        return this;
    }

    public ExtensionBuilder AddContextContributor<TContributor>() where TContributor : class, IContextContributor => AddContextContributor(typeof(TContributor));
    public ExtensionBuilder AddContextContributor(IContextContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        _contextContributors.Add(new ContextContributorRegistration(_extensionId, contributor.GetType(), contributor));
        return this;
    }
    public ExtensionBuilder AddContextContributor(Type contributorType)
    {
        ArgumentNullException.ThrowIfNull(contributorType);
        if (!typeof(IContextContributor).IsAssignableFrom(contributorType))
            throw new ArgumentException($"Type must implement {nameof(IContextContributor)}.", nameof(contributorType));
        _contextContributors.Add(new ContextContributorRegistration(_extensionId, contributorType));
        return this;
    }

    public ExtensionBuilder AddWorkflow(WorkflowDefinition definition) { _workflows.Add(definition); return this; }
    public ExtensionBuilder AddSkill(SkillDocument skill) { _skills.Add(skill); return this; }
    public ExtensionBuilder AddMessageTagRenderer(IMessageTagRenderer renderer) { _tagRenderers.Add(renderer); return this; }
    public ExtensionBuilder AddStateMutationHandler(IStateMutationHandler handler) { _stateHandlers.Add(new(_extensionId, handler)); return this; }

    public IReadOnlyList<CapabilityDescriptor> Capabilities => _capabilities;
    public IReadOnlyList<(CapabilityDescriptor Descriptor, ICapabilityInvoker Invoker)> InvokableCapabilities => _invokableCapabilities;
    public IReadOnlyList<ContextContributorRegistration> ContextContributors => _contextContributors;
    public IReadOnlyList<WorkflowDefinition> Workflows => _workflows;
    public IReadOnlyList<SkillDocument> Skills => _skills;
    public IReadOnlyList<IMessageTagRenderer> TagRenderers => _tagRenderers;
    public IReadOnlyList<StateMutationHandlerRegistration> StateHandlers => _stateHandlers;
}

public sealed record ContextContributorRegistration(string ExtensionId, Type ContributorType, IContextContributor? Instance = null);
public sealed record StateMutationHandlerRegistration(string ExtensionId, IStateMutationHandler Handler);

public interface IStateMutationHandler
{
    string SubjectPrefix { get; }
    Task<string?> ValidateAsync(MutationIntent intent, StateSnapshot? current, CancellationToken ct = default);
}

internal sealed class NullInvoker : ICapabilityInvoker
{
    public static readonly NullInvoker Instance = new();
    public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default) =>
        Task.FromResult(new CapabilityResult { InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Failed, Error = $"capability '{call.CanonicalId}' has no invoker" });
}
