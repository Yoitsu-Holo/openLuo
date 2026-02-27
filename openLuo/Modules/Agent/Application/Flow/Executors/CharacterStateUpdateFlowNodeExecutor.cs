using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterStateUpdateFlowNodeExecutor : IAgentFlowNodeExecutor
{
    public string CallName => "character.state_update";

    private readonly CharacterStateUpdateNode _stateUpdateNode;

    public CharacterStateUpdateFlowNodeExecutor(CharacterStateUpdateNode stateUpdateNode)
    {
        _stateUpdateNode = stateUpdateNode;
    }

    public async Task<AgentFlowNodeExecutionResult> ExecuteAsync(
        AgentFlowNode node,
        AgentFlowRunRequest request,
        IReadOnlyDictionary<string, object?> state,
        CancellationToken ct = default)
    {
        if (!CharacterFlowExecutionHelpers.TryGetTurnContext(state, out var turnContext))
            return AgentFlowNodeExecutionResult.Fail("Missing turnContext in flow state.");

        var toolResult = CharacterFlowExecutionHelpers.GetToolResultOrDefault(state);
        var finalReply = CharacterFlowExecutionHelpers.GetFinalReplyOrDefault(state, toolResult);
        var visibleBlocks = state.TryGetValue("executionVisibleBlocks", out var blocksObj) && blocksObj is IReadOnlyList<string> blocks
            ? blocks
            : toolResult.VisibleBlocks;
        var presentation = state.TryGetValue("executionPresentation", out var presentationObj) && presentationObj is CommandPresentation existingPresentation
            ? existingPresentation
            : toolResult.Presentation;
        var stateUpdate = await _stateUpdateNode.ExecuteAsync(turnContext, finalReply, toolResult, ct);
        var turnResult = new CharacterTurnResult
        {
            Reply = finalReply,
            Presentation = presentation,
            InterAgentOutcome = toolResult.InterAgentOutcome,
            Steps = toolResult.Steps,
            VisibleBlocks = visibleBlocks,
            PendingAbility = toolResult.PendingAbility,
            EndDialogue = toolResult.EndDialogue,
            ShouldContinueToolLoop = toolResult.ShouldContinueToolLoop,
            Memory = turnContext.Memory,
            TodoList = turnContext.TodoList,
            StateUpdate = stateUpdate
        };

        return AgentFlowNodeExecutionResult.Ok(
            turnResult,
            new Dictionary<string, object?>
            {
                ["stateUpdate"] = stateUpdate,
                ["finalReply"] = finalReply,
                ["turnResult"] = turnResult
            });
    }
}
