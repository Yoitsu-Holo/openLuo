using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterAgent : ICharacterAgent
{
    private readonly ICharacterMemoryGateway _memoryGateway;
    private readonly ICharacterStateGateway _stateGateway;
    private readonly ICharacterCapabilitySnapshotProvider _capabilitySnapshotProvider;
    private readonly ICharacterPromptContextBuilder _promptContextBuilder;
    private readonly IAgentFlowRunner _flowRunner;
    private readonly ITurnMessageEmitterFactory _messageEmitterFactory;

    public CharacterAgent(
        ICharacterMemoryGateway memoryGateway,
        ICharacterStateGateway stateGateway,
        ICharacterCapabilitySnapshotProvider capabilitySnapshotProvider,
        ICharacterPromptContextBuilder promptContextBuilder,
        IAgentFlowRunner flowRunner,
        ITurnMessageEmitterFactory messageEmitterFactory)
    {
        _memoryGateway = memoryGateway;
        _stateGateway = stateGateway;
        _capabilitySnapshotProvider = capabilitySnapshotProvider;
        _promptContextBuilder = promptContextBuilder;
        _flowRunner = flowRunner;
        _messageEmitterFactory = messageEmitterFactory;
    }

    public async Task<CharacterTurnResult> RunTurnAsync(CharacterTurnRequest request, CancellationToken ct = default)
    {
        var memory = request.Memory;
        var currentStateSummary = await _stateGateway.BuildStateSummaryAsync(request, ct);
        var capabilitySnapshot = await _capabilitySnapshotProvider.LoadAsync(request, ct);

        var context = new CharacterTurnContext
        {
            Request = request,
            Profile = new CharacterAgentProfile
            {
                CharacterId = request.Profile.CharacterId,
                DisplayName = request.Profile.DisplayName,
                ArchetypeId = request.Profile.ArchetypeId,
                RolePrompt = request.Profile.RolePrompt
            },
            State = new CharacterAgentState
            {
                GameId = request.Context.GameId,
                CharacterId = request.Context.CharacterId,
                Summary = request.Context.Summary,
                Conversation = request.Context.Conversation
            },
            Memory = memory,
            CurrentStateSummary = currentStateSummary,
            CapabilitySnapshot = capabilitySnapshot,
            PromptContext = await _promptContextBuilder.BuildAsync(request, memory, capabilitySnapshot, currentStateSummary, ct)
        };

        var flowRequest = new AgentFlowRunRequest
        {
            FlowId = ResolveFlowId(request.Message.Type),
            AgentId = request.Context.CharacterId,
            GameId = request.Context.GameId,
            TurnId = request.Message.CorrelationId ?? request.Message.MessageId,
            ExecutionContext = request.ExecutionContext,
            SessionId = request.Message.Metadata?.GetValueOrDefault("sessionId"),
            ChannelId = request.Message.Metadata?.GetValueOrDefault("channelId"),
            Inputs = new Dictionary<string, object?>
            {
                ["turnContext"] = context,
                ["presentationProfile"] = request.Message.Metadata?.GetValueOrDefault("presentationProfile") ?? string.Empty
            }
        };
        flowRequest = new AgentFlowRunRequest
        {
            FlowId = flowRequest.FlowId,
            AgentId = flowRequest.AgentId,
            GameId = flowRequest.GameId,
            TurnId = flowRequest.TurnId,
            ExecutionContext = flowRequest.ExecutionContext,
            MessageEmitter = _messageEmitterFactory.Create(flowRequest),
            Inputs = flowRequest.Inputs,
            MaxStepsOverride = flowRequest.MaxStepsOverride,
            SessionId = flowRequest.SessionId,
            ChannelId = flowRequest.ChannelId
        };

        var flowResult = await _flowRunner.RunAsync(flowRequest, ct);

        if (flowResult.Success &&
            flowResult.Outputs.TryGetValue("turnResult", out var output) &&
            output is CharacterTurnResult turnResult)
        {
            return new CharacterTurnResult
            {
                Reply = turnResult.Reply,
                Presentation = turnResult.Presentation,
                InterAgentOutcome = turnResult.InterAgentOutcome,
                Steps = turnResult.Steps,
                VisibleBlocks = turnResult.VisibleBlocks,
                PendingAbility = turnResult.PendingAbility,
                EndDialogue = turnResult.EndDialogue,
                Memory = turnResult.Memory,
                TodoList = turnResult.TodoList,
                StateUpdate = turnResult.StateUpdate,
                HasStreamedPublicOutput = flowRequest.MessageEmitter?.HasPublishedPublicMessage == true
            };
        }

        var effectiveMemory = flowResult.Outputs.TryGetValue("memory", out var memoryOutput) &&
                              memoryOutput is CharacterMemorySnapshot latestMemory
            ? latestMemory
            : context.Memory;

        return new CharacterTurnResult
        {
            Reply = "我现在还需要一点时间整理思绪。",
            Memory = effectiveMemory,
            HasStreamedPublicOutput = flowRequest.MessageEmitter?.HasPublishedPublicMessage == true
        };
    }

    private static string ResolveFlowId(AgentMessageType messageType) =>
        messageType == AgentMessageType.AgentAsk
            ? CharacterAgentAskFlow.FlowId
            : CharacterStandardChatFlow.FlowId;
}
