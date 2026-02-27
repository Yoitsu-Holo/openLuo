using openLuo.Modules.Executor.Application.TODOList;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterTODOListNode
{
    private readonly IExecutor<TODOListInput, TODOListOutput> _todoListExecutor;
    private readonly IRuntimeConfigCenter _config;

    public CharacterTODOListNode(
        IExecutor<TODOListInput, TODOListOutput> todoListExecutor,
        IRuntimeConfigCenter config)
    {
        _todoListExecutor = todoListExecutor;
        _config = config;
    }

    public async Task<TODOListOutput> ExecuteAsync(CharacterTurnContext context, CancellationToken ct = default)
    {
        var executors = _config.GetSnapshot().Executors;
        var conversation = context.PromptContext.Conversation
            .Select(x => $"{x.Role}: {x.Content}")
            .ToList();

        var toolCapabilities = context.CapabilitySnapshot.Capabilities
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Where(c => !string.Equals(c.Name, "narrative_chat", StringComparison.OrdinalIgnoreCase))
            .Select(c => string.IsNullOrWhiteSpace(c.HelpShort)
                ? c.Name.Trim()
                : $"{c.HelpShort.Trim()}")
            .ToList();

        var result = await _todoListExecutor.ExecuteAsync(new TODOListInput
        {
            Temperature = executors.TODOList.Temperature,
            MaxTokens = executors.TODOList.MaxTokens,
            CharacterProfile = context.PromptContext.CharacterProfile,
            WorldContext = context.PromptContext.WorldContext,
            SceneState = context.PromptContext.SceneState,
            GoalContext = context.PromptContext.GoalContext,
            MemorySummary = context.Memory.Summary,
            CurrentStateSummary = context.CurrentStateSummary,
            ToolCapabilities = toolCapabilities,
            Conversation = conversation,
            PlayerInput = context.PromptContext.PlayerInput
        }, ct);

        if (result.Success && result.Output is not null)
            return result.Output;

        return new TODOListOutput { Todos = ["回复玩家"] };
    }
}
