using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Agent.Application;

public interface ICharacterTurnRequestBuilder
{
    Task<CharacterTurnRequest> BuildAsync(AgentContext context, AgentMessage message, CancellationToken ct = default);
}

public interface ICharacterTurnResultApplier
{
    Task<AgentMessage> ApplyAsync(
        AgentContext context,
        AgentMessage message,
        CharacterTurnResult result,
        CancellationToken ct = default);
}

public sealed class DefaultCharacterTurnRequestBuilder : ICharacterTurnRequestBuilder
{
    private readonly ICharacterMemoryGateway _memoryGateway;
    private readonly IAgentProfileCatalog _profileCatalog;
    private readonly ICosplaySkillProvider _cosplaySkillProvider;

    public DefaultCharacterTurnRequestBuilder(
        ICharacterMemoryGateway memoryGateway,
        IAgentProfileCatalog profileCatalog,
        ICosplaySkillProvider cosplaySkillProvider)
    {
        _memoryGateway = memoryGateway;
        _profileCatalog = profileCatalog;
        _cosplaySkillProvider = cosplaySkillProvider;
    }

    public async Task<CharacterTurnRequest> BuildAsync(AgentContext context, AgentMessage message, CancellationToken ct = default)
    {
        var profile = _profileCatalog.GetProfile(context.CharacterId);
        var memory = await _memoryGateway.LoadAsync(context, message, ct);

        return new CharacterTurnRequest
        {
            Context = context,
            Profile = profile,
            Message = message,
            ExecutionContext = message.ExecutionContext,
            Memory = memory,
            PreloadedSkills = _cosplaySkillProvider.GetPreloadedSkills(profile),
            ExtraContexts = message.ContextBlocks ?? []
        };
    }
}

public sealed class DefaultCharacterTurnResultApplier : ICharacterTurnResultApplier
{
    private readonly IAgentMemoryStore _memoryStore;

    public DefaultCharacterTurnResultApplier(IAgentMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    public async Task<AgentMessage> ApplyAsync(
        AgentContext context,
        AgentMessage message,
        CharacterTurnResult result,
        CancellationToken ct = default)
    {
        if (result.Steps.Count > 0)
            context.Summary = MergeSummary(context.Summary, result.Steps, result.InterAgentOutcome);

        if (!CharacterTurnPolicy.IsInternalLoopMessage(message.Type))
        {
            await _memoryStore.StoreAgentEventAsync(
                context.GameId,
                context.CharacterId,
                $"{message.From}: {message.Payload}",
                message.Type == AgentMessageType.Chat ? 1 : 2,
                ct);

        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (result.HasStreamedPublicOutput)
            metadata["streamedPublicOutput"] = "true";

        return new AgentMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            GameId: message.GameId,
            From: context.CharacterId,
            To: message.From,
            Type: ResolveReplyType(message.Type),
            Payload: result.Reply,
            CorrelationId: message.CorrelationId,
            TimestampUtc: DateTimeOffset.UtcNow,
            TraceLines: CharacterTurnTraceBuilder.Build(result.Steps),
            VisibleBlocks: [.. FormatVisibleBlocks(result.Steps), .. result.VisibleBlocks],
            Presentation: result.Presentation,
            EndDialogue: result.EndDialogue,
            PendingAbility: result.PendingAbility,
            InterAgentOutcome: result.InterAgentOutcome,
            Metadata: metadata);
    }

    private static string MergeSummary(
        string currentSummary,
        IReadOnlyList<AgentToolUseStep> steps,
        InterAgentOutcome? outcome)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(currentSummary))
            segments.AddRange(currentSummary.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var step in steps)
            segments.Add($"tool/{step.Name}:{(step.Success ? "ok" : "fail")} {step.Summary}");

        return string.Join(" | ", segments.Distinct(StringComparer.OrdinalIgnoreCase).TakeLast(10));
    }

    private static IReadOnlyList<string> FormatVisibleBlocks(IReadOnlyList<AgentToolUseStep> steps) =>
        steps.Select(step => step.VisibleOutput)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

    private static AgentMessageType ResolveReplyType(AgentMessageType inboundType) =>
        inboundType switch
        {
            AgentMessageType.TaskAssign => AgentMessageType.TaskResult,
            AgentMessageType.AgentAsk => AgentMessageType.AgentReply,
            AgentMessageType.AgentDialogueTurn => AgentMessageType.AgentDialogueTurn,
            _ => AgentMessageType.Chat
        };
}
