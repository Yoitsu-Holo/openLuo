using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.Executor.Application.TODOList;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterExecFlowNodeExecutor : IAgentFlowNodeExecutor
{
    public string CallName => "character.exec";

    private readonly CharacterExecNode _execNode;

    public CharacterExecFlowNodeExecutor(CharacterExecNode execNode)
    {
        _execNode = execNode;
    }

    public async Task<AgentFlowNodeExecutionResult> ExecuteAsync(
        AgentFlowNode node,
        AgentFlowRunRequest request,
        IReadOnlyDictionary<string, object?> state,
        CancellationToken ct = default)
    {
        if (!CharacterFlowExecutionHelpers.TryGetTurnContext(state, out var turnContext))
            return AgentFlowNodeExecutionResult.Fail("Missing turnContext in flow state.");

        if (!state.TryGetValue("todoList", out var todoObj) || todoObj is not TODOListOutput todoList)
            return AgentFlowNodeExecutionResult.Fail("Missing todoList in flow state.");

        var result = await _execNode.ExecuteAsync(turnContext, todoList, ct);
        await TurnMessagePublisher.PublishPresentationAsync(node, request, result.Presentation, ct);

        return AgentFlowNodeExecutionResult.Ok(
            result,
            new Dictionary<string, object?>
            {
                ["toolResult"] = result.ToolResult,
                ["finalReply"] = result.FinalReply,
                ["executionVisibleBlocks"] = result.VisibleBlocks,
                ["executionPresentation"] = result.Presentation,
                ["todoList"] = todoList
            });
    }
}
