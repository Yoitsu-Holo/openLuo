using openLuo.Core.Models;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.Agent.Application;

public sealed class AgentChatTurnContext
{
    public required string GameId { get; init; }
    public required Character TargetCharacter { get; init; }
    public required GameState State { get; init; }
    public required string PlayerMessage { get; init; }
    public required string CorrelationId { get; init; }
    public SessionPresentationProfile PresentationProfile { get; init; } = SessionPresentationProfile.Default;
}

public sealed class AgentChatTurnBeforeResult
{
    public string? OverriddenPlayerMessage { get; init; }
    public IReadOnlyList<AgentContextBlock> ExtraContexts { get; init; } = [];
    public IReadOnlyList<string> VisibleBlocks { get; init; } = [];
}

public sealed class AgentChatTurnAfterContext
{
    public required AgentChatTurnContext Turn { get; init; }
    public required string FinalReply { get; init; }
    public required IReadOnlyList<string> VisibleBlocks { get; init; }
    public required IReadOnlyList<string> TraceLines { get; init; }
}

public sealed class AgentChatTurnAfterResult
{
    public IReadOnlyList<string> VisibleBlocks { get; init; } = [];
}

public interface IAgentChatHook
{
    Task<AgentChatTurnBeforeResult> OnChatTurnBeforeAsync(AgentChatTurnContext context, CancellationToken ct = default);

    Task<AgentChatTurnAfterResult> OnChatTurnAfterAsync(AgentChatTurnAfterContext context, CancellationToken ct = default);
}

public interface IAgentChatHookStage
{
    Task<AgentChatTurnBeforeResult> OnChatTurnBeforeAsync(AgentChatTurnContext context, CancellationToken ct = default);

    Task<AgentChatTurnAfterResult> OnChatTurnAfterAsync(AgentChatTurnAfterContext context, CancellationToken ct = default);
}

public sealed class NoOpAgentChatHook : IAgentChatHook
{
    public Task<AgentChatTurnBeforeResult> OnChatTurnBeforeAsync(AgentChatTurnContext context, CancellationToken ct = default) =>
        Task.FromResult(new AgentChatTurnBeforeResult());

    public Task<AgentChatTurnAfterResult> OnChatTurnAfterAsync(AgentChatTurnAfterContext context, CancellationToken ct = default) =>
        Task.FromResult(new AgentChatTurnAfterResult());
}

public sealed class NoOpAgentChatHookStage : IAgentChatHookStage
{
    public Task<AgentChatTurnBeforeResult> OnChatTurnBeforeAsync(AgentChatTurnContext context, CancellationToken ct = default) =>
        Task.FromResult(new AgentChatTurnBeforeResult());

    public Task<AgentChatTurnAfterResult> OnChatTurnAfterAsync(AgentChatTurnAfterContext context, CancellationToken ct = default) =>
        Task.FromResult(new AgentChatTurnAfterResult());
}

public sealed class CompositeAgentChatHook : IAgentChatHook
{
    private readonly IReadOnlyList<IAgentChatHookStage> _stages;

    public CompositeAgentChatHook(IEnumerable<IAgentChatHookStage> stages)
    {
        _stages = stages.ToList();
    }

    public async Task<AgentChatTurnBeforeResult> OnChatTurnBeforeAsync(AgentChatTurnContext context, CancellationToken ct = default)
    {
        string? overriddenPlayerMessage = null;
        var extraContexts = new List<AgentContextBlock>();
        var visibleBlocks = new List<string>();

        foreach (var stage in _stages)
        {
            var result = await stage.OnChatTurnBeforeAsync(context, ct);
            if (!string.IsNullOrWhiteSpace(result.OverriddenPlayerMessage))
                overriddenPlayerMessage = result.OverriddenPlayerMessage.Trim();

            if (result.ExtraContexts.Count > 0)
                extraContexts.AddRange(result.ExtraContexts.Where(x => !string.IsNullOrWhiteSpace(x.Content)));

            if (result.VisibleBlocks.Count > 0)
                visibleBlocks.AddRange(result.VisibleBlocks.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        return new AgentChatTurnBeforeResult
        {
            OverriddenPlayerMessage = overriddenPlayerMessage,
            ExtraContexts = extraContexts,
            VisibleBlocks = visibleBlocks
        };
    }

    public async Task<AgentChatTurnAfterResult> OnChatTurnAfterAsync(AgentChatTurnAfterContext context, CancellationToken ct = default)
    {
        var visibleBlocks = new List<string>();
        foreach (var stage in _stages)
        {
            var result = await stage.OnChatTurnAfterAsync(context, ct);
            if (result.VisibleBlocks.Count > 0)
                visibleBlocks.AddRange(result.VisibleBlocks.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        return new AgentChatTurnAfterResult
        {
            VisibleBlocks = visibleBlocks
        };
    }
}

public sealed class AgentStepContext
{
    public required string GameId { get; init; }
    public required string CharacterId { get; init; }
    public required string MessageType { get; init; }
    public required string MessagePayload { get; init; }
    public required IReadOnlyList<AgentCapabilityDescriptor> AvailableCapabilities { get; init; }
    public required AgentProfile Profile { get; init; }
}

public sealed class AgentStepAfterContext
{
    public required AgentStepContext Step { get; init; }
    public required AgentToolUseResult Result { get; init; }
}

public interface IAgentStepHook
{
    Task OnAgentStepBeforeAsync(AgentStepContext context, CancellationToken ct = default);

    Task OnAgentStepAfterAsync(AgentStepAfterContext context, CancellationToken ct = default);
}

public sealed class NoOpAgentStepHook : IAgentStepHook
{
    public Task OnAgentStepBeforeAsync(AgentStepContext context, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task OnAgentStepAfterAsync(AgentStepAfterContext context, CancellationToken ct = default) =>
        Task.CompletedTask;
}
