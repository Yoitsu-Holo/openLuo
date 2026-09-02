using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Abstractions;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;
using CapabilityMutationIntent = openLuo.Capabilities.Core.Models.MutationIntent;
using CapabilityStateSnapshot = openLuo.Capabilities.Core.Models.StateSnapshot;
namespace OpenLuo.Extensions.World;
public sealed class WorldExtension : IAgentExtension
{
    private readonly IStateQueryService _query;
    private readonly IStateMutationService _mutation;

    public WorldExtension(IStateQueryService query, IStateMutationService mutation)
    {
        _query = query;
        _mutation = mutation;
    }

    public void Configure(ExtensionBuilder builder)
    {
        builder.AddCapability(WorldDescriptors.Read, new StateReadInvoker(_query));
        builder.AddCapability(WorldDescriptors.Update, new StateUpdateInvoker(_mutation));
        builder.AddContextContributor(new WorldStateContributor(_query));
        builder.AddStateMutationHandler(new WorldMutationHandler());
    }
}

internal static class WorldDescriptors
{
    public static CapabilityDescriptor Read => new()
    {
        CanonicalId = "state.read", DisplayName = "World state read",
        Summary = "Read current state values for the active subject.", Usage = "Use before decisions that depend on current state.",
        Kind = CapabilityKind.Builtin, ProviderId = "world", SideEffect = SideEffectClass.ReadOnly,
        Completion = CompletionPolicy.Continue,
        InputSchema = new { type = "object" }
    };

    public static CapabilityDescriptor Update => new()
    {
        CanonicalId = "state.propose_update", DisplayName = "World state update",
        Summary = "Propose a state value change for atomic validation and commit.", Usage = "Use when a world value must change.",
        Kind = CapabilityKind.Builtin, ProviderId = "world", SideEffect = SideEffectClass.Mutation,
        Completion = CompletionPolicy.Continue,
        Idempotency = IdempotencyKind.NonIdempotent, InputSchema = new { type = "object" }
    };
}

internal sealed class StateReadInvoker : ICapabilityInvoker
{
    private readonly IStateQueryService _service;
    public StateReadInvoker(IStateQueryService service) => _service = service;

    public async Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var key = call.Options.TryGetValue("key", out var optionKey) ? optionKey : string.Join(" ", call.Args);
        if (string.IsNullOrWhiteSpace(key))
            return new CapabilityResult { InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Rejected, Error = "key is required" };
        var result = await _service.GetAsync(context.GameId, "world", StateOwnerKind.Character, context.SubjectId, key);
        return new CapabilityResult { InvocationId = call.InvocationId, Success = true, Status = CapabilityStatus.Ok, Text = $"{key}={result.Value ?? "<unset>"}" };
    }
}

internal sealed class StateUpdateInvoker : ICapabilityInvoker
{
    private readonly IStateMutationService _service;
    public StateUpdateInvoker(IStateMutationService service) => _service = service;

    public async Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var key = call.Options.TryGetValue("key", out var optionKey) ? optionKey : string.Empty;
        var value = call.Options.TryGetValue("value", out var optionValue) ? optionValue : string.Empty;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return new CapabilityResult { InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Rejected, Error = "key and value are required" };

        var results = await _service.ApplyAsync(context.GameId, [new StateMutation
        {
            Namespace = "world", Key = key, Value = value, Op = call.Options.GetValueOrDefault("op", "set"),
            OwnerKind = StateOwnerKind.Character, OwnerId = context.SubjectId, Reason = "agent capability world:state.propose_update"
        }]);
        var result = results.FirstOrDefault();
        return new CapabilityResult
        {
            InvocationId = call.InvocationId, Success = result?.Ok == true,
            Status = result?.Ok == true ? CapabilityStatus.Ok : CapabilityStatus.Failed,
            Text = result?.Ok == true ? $"{key}={result.NewValue}" : null, Error = result?.Ok == true ? null : result?.Error ?? "state update failed"
        };
    }
}

public sealed class WorldStateContributor : IContextContributor
{
    private readonly IStateQueryService _query;
    public WorldStateContributor(IStateQueryService query) => _query = query;
    public string Id => "world:state";

    public async Task<ContextContributionResult> ContributeAsync(ContextBuildRequest request, CancellationToken ct = default)
    {
        try
        {
            var states = await _query.QueryAsync(request.SessionId, "world", StateOwnerKind.Character, request.SubjectId, keys: null, includeDefaults: true);
            var text = string.Join(", ", states.Select(s => $"{s.Key}={s.Value}"));
            return new ContextContributionResult
            {
                State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok },
                Contributions = string.IsNullOrWhiteSpace(text) ? [] : [new ContextContribution
                {
                    Id = "world-state", ContributorId = Id, Region = ContextRegion.SceneState,
                    Content = text, Priority = 60, TokenEstimate = Math.Max(1, text.Length / 4)
                }]
            };
        }
        catch (Exception ex)
        {
            return new ContextContributionResult { State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Unavailable, Reason = ex.Message, Retryable = true } };
        }
    }
}

public sealed class WorldMutationHandler : IStateMutationHandler
{
    public string SubjectPrefix => "world";
    public Task<string?> ValidateAsync(CapabilityMutationIntent intent, CapabilityStateSnapshot? current, CancellationToken ct = default)
    {
        if (!intent.ResourcePath.StartsWith("world:", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<string?>("world mutation must use world: resource path");
        return Task.FromResult<string?>(null);
    }
}
