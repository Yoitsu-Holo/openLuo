using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterTODOListFlowNodeExecutor : IAgentFlowNodeExecutor
{
    public string CallName => "character.todo_list";

    private readonly CharacterTODOListNode _todoListNode;

    public CharacterTODOListFlowNodeExecutor(CharacterTODOListNode todoListNode)
    {
        _todoListNode = todoListNode;
    }

    public async Task<AgentFlowNodeExecutionResult> ExecuteAsync(
        AgentFlowNode node,
        AgentFlowRunRequest request,
        IReadOnlyDictionary<string, object?> state,
        CancellationToken ct = default)
    {
        if (!CharacterFlowExecutionHelpers.TryGetTurnContext(state, out var turnContext))
            return AgentFlowNodeExecutionResult.Fail("Missing turnContext in flow state.");

        var todoList = await _todoListNode.ExecuteAsync(turnContext, ct);
        var updatedContext = CharacterFlowExecutionHelpers.CloneTurnContext(turnContext, todoList: todoList);

        return AgentFlowNodeExecutionResult.Ok(
            todoList,
            new Dictionary<string, object?>
            {
                ["todoList"] = todoList,
                ["turnContext"] = updatedContext
            });
    }
}
