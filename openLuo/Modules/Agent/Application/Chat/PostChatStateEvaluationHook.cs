using openLuo.Core.Interfaces;
using openLuo.Modules.Gameplay.Core.Interfaces;

namespace openLuo.Modules.Agent.Application;

public sealed class PostChatStateEvaluationHook : IAgentChatHookStage
{
    private readonly IStateEvaluationCoordinator _stateEvaluationCoordinator;
    private readonly IGameLogger _logger;

    public PostChatStateEvaluationHook(
        IStateEvaluationCoordinator stateEvaluationCoordinator,
        IGameLogger logger)
    {
        _stateEvaluationCoordinator = stateEvaluationCoordinator;
        _logger = logger;
    }

    public Task<AgentChatTurnBeforeResult> OnChatTurnBeforeAsync(AgentChatTurnContext context, CancellationToken ct = default) =>
        Task.FromResult(new AgentChatTurnBeforeResult());

    public async Task<AgentChatTurnAfterResult> OnChatTurnAfterAsync(AgentChatTurnAfterContext context, CancellationToken ct = default)
    {
        try
        {
            var beatSummary = BuildBeatSummary(context);
            var moodSignals = ExtractMoodSignals(context.FinalReply);
            var result = await _stateEvaluationCoordinator.EvaluateStatesAsync(
                context.Turn.GameId,
                context.Turn.TargetCharacter.Id,
                context.Turn.TargetCharacter.ArchetypeId,
                beatSummary,
                [.. moodSignals],
                context.Turn.PlayerMessage,
                "chat",
                ct);

            if (result.AppliedChanges.Length == 0)
                return new AgentChatTurnAfterResult();

            var summary = BuildDeltaSummary(result.AppliedChanges);
            _logger.Info("chat/hook", $"post chat state changes: {summary}", new
            {
                gameId = context.Turn.GameId,
                characterId = context.Turn.TargetCharacter.Id,
                correlationId = context.Turn.CorrelationId,
                changes = result.AppliedChanges.Select(change => new
                {
                    change.ResourceId,
                    change.Namespace,
                    change.Op,
                    change.OldValue,
                    change.NewValue
                }).ToArray()
            });

            return new AgentChatTurnAfterResult
            {
                VisibleBlocks = [$"[状态结算] {summary}"]
            };
        }
        catch (Exception ex)
        {
            _logger.Warn("chat/hook", $"post chat state evaluation skipped: {ex.Message}");
            return new AgentChatTurnAfterResult();
        }
    }

    private static string BuildBeatSummary(AgentChatTurnAfterContext context)
    {
        var blocks = context.VisibleBlocks
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (!string.IsNullOrWhiteSpace(context.FinalReply))
            blocks.Add(context.FinalReply.Trim());
        return string.Join("\n", blocks);
    }

    private static IReadOnlyList<string> ExtractMoodSignals(string reply)
    {
        var signals = new List<string>();
        var source = reply ?? string.Empty;
        AddIfAny(signals, source, "warm", "温柔", "温和", "安心", "心意", "谢谢");
        AddIfAny(signals, source, "happy", "开心", "高兴", "愉快", "笑");
        AddIfAny(signals, source, "shy", "脸红", "害羞", "耳热");
        AddIfAny(signals, source, "curious", "好奇", "疑惑");
        AddIfAny(signals, source, "awkward", "尴尬", "不自在");
        AddIfAny(signals, source, "worried", "担心", "不安");
        AddIfAny(signals, source, "annoyed", "不满", "生气", "恼火");
        AddIfAny(signals, source, "sad", "难过", "失落", "受伤");
        AddIfAny(signals, source, "jealous", "吃醋", "嫉妒");
        if (signals.Count == 0)
            signals.Add("neutral");
        return signals;
    }

    private static void AddIfAny(List<string> signals, string source, string signal, params string[] keywords)
    {
        if (keywords.Any(keyword => source.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            signals.Add(signal);
    }

    private static string BuildDeltaSummary(IReadOnlyList<StateAppliedChange> changes)
    {
        var parts = changes
            .Select(change =>
            {
                var before = change.OldValue ?? "null";
                var after = change.NewValue ?? "null";
                return $"{change.ResourceId}: {before} -> {after}";
            })
            .ToList();
        return string.Join("；", parts);
    }
}
