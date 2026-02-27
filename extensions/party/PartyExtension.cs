using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Abstractions;
using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;
using AgentOutputVisibility = openLuo.Capabilities.Core.Models.OutputVisibility;
using openLuo.Modules.Agent.Application.Runtime;

namespace OpenLuo.Extensions.Party;

public sealed class PartyExtension : IAgentExtension
{
    private readonly IAgentRoster _roster;
    private readonly IAgentRuntimeHub _runtimeHub;

    public PartyExtension(IAgentRoster roster, IAgentRuntimeHub runtimeHub)
    {
        _roster = roster;
        _runtimeHub = runtimeHub;
    }

    public void Configure(ExtensionBuilder builder)
    {
        builder.AddCapability(PartyDescriptors.List, new ListCharactersInvoker(_roster));
        builder.AddCapability(PartyDescriptors.Ask, new AskCharacterInvoker(_roster, _runtimeHub));
        builder.AddContextContributor(new PartyRosterContributor(_roster));
    }
}

internal static class PartyDescriptors
{
    public static CapabilityDescriptor List => new()
    {
        CanonicalId = "list_characters", DisplayName = "List characters",
        Summary = "List characters available in the current party.", Usage = "Use before delegating to a character.",
        Kind = CapabilityKind.Builtin, ProviderId = "party", SideEffect = SideEffectClass.ReadOnly,
        Completion = CompletionPolicy.Continue, Visibility = AgentOutputVisibility.Silent, InputSchema = new { type = "object" }
    };

    public static CapabilityDescriptor Ask => new()
    {
        CanonicalId = "ask_character", DisplayName = "Ask character",
        Summary = "Ask another character a question and return its response.", Usage = "Use when another character's knowledge or viewpoint is needed.",
        Kind = CapabilityKind.RemoteAgent, ProviderId = "party", SideEffect = SideEffectClass.Delegation,
        Completion = CompletionPolicy.Continue, Visibility = AgentOutputVisibility.Silent, ParallelSafe = false,
        InputSchema = new { type = "object" }
    };
}

internal sealed class ListCharactersInvoker : ICapabilityInvoker
{
    private readonly IAgentRoster _roster;
    public ListCharactersInvoker(IAgentRoster roster) => _roster = roster;

    public async Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var characters = await _roster.ListAsync(context.GameId, ct);
        var text = string.Join("\n", characters.Select(c => $"{c.Id}: {c.Name}"));
        return new CapabilityResult { InvocationId = call.InvocationId, Success = true, Status = CapabilityStatus.Ok, Text = text };
    }
}

internal sealed class AskCharacterInvoker : ICapabilityInvoker
{
    private readonly IAgentRoster _roster;
    private readonly IAgentRuntimeHub _hub;
    public AskCharacterInvoker(IAgentRoster roster, IAgentRuntimeHub hub) { _roster = roster; _hub = hub; }

    public async Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var target = call.Options.GetValueOrDefault("character", string.Empty);
        var question = call.Options.GetValueOrDefault("question", string.Join(" ", call.Args));
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(question))
            return new CapabilityResult { InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Rejected, Error = "character and question are required" };
        var character = await _roster.ResolveAsync(context.GameId, target, ct);
        if (character is null)
            return new CapabilityResult { InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Rejected, Error = $"character not found: {target}" };

        var response = await _hub.RequestAsync(character.Id, AgentMessageType.AgentAsk, "party", question, context.GameId, context.TurnId, TimeSpan.FromSeconds(30), ct);
        return new CapabilityResult
        {
            InvocationId = call.InvocationId, Success = response is not null, Status = response is null ? CapabilityStatus.Failed : CapabilityStatus.Ok,
            Text = response?.Payload, Error = response is null ? "character did not respond" : null
        };
    }
}

public sealed class PartyRosterContributor : IContextContributor
{
    private readonly IAgentRoster _roster;
    public PartyRosterContributor(IAgentRoster roster) => _roster = roster;
    public string Id => "party:roster";

    public async Task<ContextContributionResult> ContributeAsync(ContextBuildRequest request, CancellationToken ct = default)
    {
        try
        {
            var roster = await _roster.ListAsync(request.SessionId, ct);
            var text = string.Join(", ", roster.Select(c => $"{c.Id}({c.Name})"));
            return new ContextContributionResult
            {
                State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok },
                Contributions = string.IsNullOrWhiteSpace(text) ? [] : [new ContextContribution
                {
                    Id = "party-roster", ContributorId = Id, Region = ContextRegion.Identity,
                    Content = $"Available characters: {text}", Priority = 50, TokenEstimate = Math.Max(1, text.Length / 4)
                }]
            };
        }
        catch (Exception ex)
        {
            return new ContextContributionResult { State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Unavailable, Reason = ex.Message, Retryable = true } };
        }
    }
}
