using openLuo.Modules.Executor.Application.StateUpdate;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterStateUpdateNode
{
    private readonly IExecutor<StateUpdateInput, StateUpdateOutput> _stateUpdateExecutor;
    private readonly IRuntimeConfigCenter _config;

    public CharacterStateUpdateNode(IExecutor<StateUpdateInput, StateUpdateOutput> stateUpdateExecutor, IRuntimeConfigCenter config)
    {
        _stateUpdateExecutor = stateUpdateExecutor;
        _config = config;
    }

    public async Task<CharacterStateUpdateResult> ExecuteAsync(
        CharacterTurnContext context,
        string finalReply,
        CharacterToolCallResult toolResult,
        CancellationToken ct = default)
    {
        var executors = _config.GetSnapshot().Executors;
        var result = await _stateUpdateExecutor.ExecuteAsync(new StateUpdateInput
        {
            Temperature = executors.StateUpdate.Temperature,
            MaxTokens = executors.StateUpdate.MaxTokens,
            CurrentStateSummary = context.CurrentStateSummary,
            SceneState = context.PromptContext.SceneState,
            PlayerInput = context.PromptContext.PlayerInput,
            CharacterResponse = finalReply,
            ToolResults = BuildToolResults(toolResult)
        }, ct);

        if (!result.Success || result.Output is null)
            return new CharacterStateUpdateResult();

        return new CharacterStateUpdateResult
        {
            Deltas = result.Output.Deltas,
            Reason = result.Output.Reason,
            Confidence = result.Output.Confidence
        };
    }

    private static IReadOnlyList<string> BuildToolResults(CharacterToolCallResult toolResult)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in toolResult.Steps)
        {
            AddUnique(results, seen, $"{step.Name}: {(step.Success ? "success" : "failed")}");
            AddUnique(results, seen, step.Summary);
        }

        if (!string.IsNullOrWhiteSpace(toolResult.Reply))
            AddUnique(results, seen, toolResult.Reply.Trim());

        foreach (var block in toolResult.VisibleBlocks)
            AddUnique(results, seen, block);

        return results;
    }

    private static void AddUnique(List<string> results, HashSet<string> seen, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = value.Trim();
        if (seen.Add(normalized))
            results.Add(normalized);
    }
}
