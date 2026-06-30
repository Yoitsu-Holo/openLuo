using openLuo.Modules.Executor.Application.CharacterResponse;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterResponseNode
{
    private readonly IExecutor<CharacterResponseInput, string> _responseExecutor;
    private readonly IRuntimeConfigCenter _config;
    internal const string DefaultFallbackReply = "我现在还需要一点时间整理思绪。";

    public CharacterResponseNode(IExecutor<CharacterResponseInput, string> responseExecutor, IRuntimeConfigCenter config)
    {
        _responseExecutor = responseExecutor;
        _config = config;
    }

    public async Task<string> ExecuteAsync(CharacterTurnContext context, CharacterToolCallResult toolResult, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(toolResult.Reply) && toolResult.EndDialogue)
            return toolResult.Reply.Trim();

        var executors = _config.GetSnapshot().Executors;
        var response = await _responseExecutor.ExecuteAsync(new CharacterResponseInput
        {
            Temperature = executors.CharacterResponse.Temperature,
            MaxTokens = executors.CharacterResponse.MaxTokens,
            CharacterProfile = context.PromptContext.CharacterProfile,
            WorldContext = context.PromptContext.WorldContext,
            SceneState = context.PromptContext.SceneState,
            GoalContext = context.PromptContext.GoalContext,
            LongTermMemory = context.Memory.Summary,
            ToolResults = BuildToolResults(toolResult),
            ExtraContexts = BuildExtraContexts(context.PromptContext.ExtraContexts),
            Conversation = context.PromptContext.Conversation.Select(ToChatMessage).ToList(),
            PlayerInput = context.PromptContext.PlayerInput,
            PlayerBlocks = context.PromptContext.PlayerBlocks
        }, ct);

        if (response.Success && !string.IsNullOrWhiteSpace(response.Output))
            return response.Output.Trim();

        return string.IsNullOrWhiteSpace(toolResult.Reply)
            ? DefaultFallbackReply
            : toolResult.Reply.Trim();
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

    private static ChatMessage ToChatMessage(AgentConversationMessage message) => new(
        message.Role == AgentConversationRole.Assistant ? ChatMessageRole.Assistant : ChatMessageRole.User,
        message.Content);

    private static IReadOnlyList<CharacterResponseContextBlock> BuildExtraContexts(IReadOnlyList<AgentContextBlock> blocks) =>
        blocks
            .Where(x => x.Rule is not EnhanceMessageRule.WorldContext and not EnhanceMessageRule.SceneState and not EnhanceMessageRule.GoalContext)
            .Select(x => new CharacterResponseContextBlock
            {
                Rule = x.Rule,
                Content = x.Content
            })
            .ToList();

    private static void AddUnique(List<string> results, HashSet<string> seen, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = value.Trim();
        if (seen.Add(normalized))
            results.Add(normalized);
    }
}
