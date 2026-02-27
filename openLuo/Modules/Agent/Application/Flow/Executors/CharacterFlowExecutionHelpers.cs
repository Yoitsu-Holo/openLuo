using openLuo.Modules.Executor.Application.TODOList;

namespace openLuo.Modules.Agent.Application;

internal static class CharacterFlowExecutionHelpers
{
    public static bool TryGetTurnContext(IReadOnlyDictionary<string, object?> state, out CharacterTurnContext turnContext)
    {
        if (state.TryGetValue("turnContext", out var contextObj) && contextObj is CharacterTurnContext context)
        {
            turnContext = context;
            return true;
        }

        turnContext = null!;
        return false;
    }

    public static CharacterToolCallResult GetToolResultOrDefault(IReadOnlyDictionary<string, object?> state) =>
        state.TryGetValue("toolResult", out var toolObj) && toolObj is CharacterToolCallResult existing
            ? existing
            : new CharacterToolCallResult();

    public static string GetFinalReplyOrDefault(IReadOnlyDictionary<string, object?> state, CharacterToolCallResult toolResult) =>
        state.TryGetValue("finalReply", out var replyObj) &&
        replyObj is string reply &&
        !string.IsNullOrWhiteSpace(reply)
            ? reply
            : toolResult.Reply;

    public static IReadOnlyList<string> GetTodosOrDefault(IReadOnlyDictionary<string, object?> state) =>
        state.TryGetValue("todoList", out var obj) && obj is TODOListOutput todoList
            ? todoList.Todos
            : [];

    public static CharacterTurnContext CloneTurnContext(
        CharacterTurnContext original,
        CharacterMemorySnapshot? memory = null) => new()
    {
        Request = original.Request,
        Profile = original.Profile,
        State = original.State,
        Memory = memory ?? original.Memory,
        CurrentStateSummary = original.CurrentStateSummary,
        CapabilitySnapshot = original.CapabilitySnapshot,
        PromptContext = original.PromptContext,
        TodoList = original.TodoList
    };

    public static CharacterTurnContext CloneTurnContext(
        CharacterTurnContext original,
        TODOListOutput? todoList) => new()
    {
        Request = original.Request,
        Profile = original.Profile,
        State = original.State,
        Memory = original.Memory,
        CurrentStateSummary = original.CurrentStateSummary,
        CapabilitySnapshot = original.CapabilitySnapshot,
        PromptContext = original.PromptContext,
        TodoList = todoList ?? original.TodoList
    };
}
