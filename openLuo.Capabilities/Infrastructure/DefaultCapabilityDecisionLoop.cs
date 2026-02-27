using System.Diagnostics;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Infrastructure;

/// <summary>
/// 默认决策循环实现（D2/D17/D18/D41）。
/// </summary>
public sealed class DefaultCapabilityDecisionLoop : ICapabilityDecisionLoop
{
    private readonly ICapabilityDecisionModel _model;
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly IContextUpdater _contextUpdater;
    private readonly IClock _clock;
    private readonly IIdempotencyRegistry _idempotency;

    public DefaultCapabilityDecisionLoop(
        ICapabilityDecisionModel model,
        ICapabilityDispatcher dispatcher,
        IContextUpdater contextUpdater,
        IClock clock,
        IIdempotencyRegistry? idempotency = null)
    {
        _model = model;
        _dispatcher = dispatcher;
        _contextUpdater = contextUpdater;
        _clock = clock;
        _idempotency = idempotency ?? new InMemoryIdempotencyRegistry();
    }

    public async Task<DecisionLoopResult> RunAsync(DecisionLoopRequest request, CancellationToken ct = default)
    {
        var budgets = request.Budgets;
        var deadline = _clock.UtcNow + budgets.OverallDeadline;
        var context = request.Context;
        var steps = new List<AgentToolUseStep>();
        var outputs = new List<OutputItem>();
        var noProgressStreak = 0;
        var decisionsUsed = 0;

        for (var decision = 1; decision <= budgets.MaxDecisions; decision++)
        {
            ct.ThrowIfCancellationRequested();
            decisionsUsed = decision;

            if (_clock.UtcNow >= deadline)
                return Finish(steps, outputs, decisionsUsed, TerminationReason.OverallTimeout, "overall deadline exceeded");

            var sw = Stopwatch.StartNew();
            CapabilityDecision decisionResult;
            try
            {
                decisionResult = await _model.DecideAsync(context, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Finish(steps, outputs, decisionsUsed, TerminationReason.Cancelled, "cancelled");
            }
            catch (Exception ex)
            {
                return Finish(steps, outputs, decisionsUsed, TerminationReason.NoProgress, $"model error: {ex.Message}");
            }
            sw.Stop();

            // D2：无 tool_call 非空文本 = 最终回复
            if (decisionResult.Calls.Count == 0 && !string.IsNullOrWhiteSpace(decisionResult.FinalText))
            {
                steps.Add(new AgentToolUseStep
                {
                    Decision = decision,
                    Action = "final_reply",
                    Name = "final_text",
                    Success = true,
                    Summary = Truncate(decisionResult.FinalText, 120),
                    DurationMs = sw.ElapsedMilliseconds
                });
                // FinalText 传完整文本（steps.Summary 是截断版，不能作为回复内容）
                return Finish(steps, outputs, decisionsUsed, TerminationReason.FinalReply, "final reply", decisionResult.FinalText);
            }

            // 空回复：继续一次受限决策（D2 配套）
            if (decisionResult.Calls.Count == 0 && string.IsNullOrWhiteSpace(decisionResult.FinalText))
            {
                noProgressStreak++;
                if (noProgressStreak >= 2)
                    return Finish(steps, outputs, decisionsUsed, TerminationReason.EmptyReply, "model returned empty reply twice");
                continue;
            }

            noProgressStreak = 0;

            // 执行批次（D7-D10）
            var batch = await _dispatcher.ExecuteBatchAsync(
                decisionResult.Calls,
                context,
                request.Catalog,
                request.BaseExecutionContext,
                ct);
            if (batch.Rejected)
            {
                steps.Add(new AgentToolUseStep
                {
                    Decision = decision,
                    Action = "rejected",
                    Name = string.Join(",", decisionResult.Calls.Select(c => c.CanonicalId)),
                    Success = false,
                    Summary = batch.RejectionReason ?? "batch rejected",
                    DurationMs = sw.ElapsedMilliseconds
                });
                // 回填拒绝原因（作为 tool 结果）：模型看到错误后可自我纠正
                // （如工具名记错、参数缺失），避免 context 不变导致 8 轮空转。
                var rejectedResults = decisionResult.Calls.Select(c => new CapabilityResult
                {
                    InvocationId = c.InvocationId,
                    Success = false,
                    Status = CapabilityStatus.Rejected,
                    Error = batch.RejectionReason ?? "batch rejected"
                }).ToList();
                context = await _contextUpdater.ApplyToolResultsAsync(
                    request.SessionId, request.TurnId, decisionResult.Calls, rejectedResults, ct);
                continue;
            }

            foreach (var result in batch.Results)
            {
                if (result.Success && result.Outputs is { Count: > 0 })
                    outputs.AddRange(result.Outputs);

                steps.Add(new AgentToolUseStep
                {
                    Decision = decision,
                    Action = "call_tool",
                    Name = result.InvocationId,
                    Success = result.Success,
                    Summary = Truncate(result.Text ?? result.Error ?? string.Empty, 120),
                    DurationMs = sw.ElapsedMilliseconds
                });
            }

            // Terminal 能力已执行 → 结束（D41 终止条件 4）
            var executed = batch.Results.Where(r => r.Success).Select(r => r.InvocationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var terminalHit = decisionResult.Calls
                .Where(c => executed.Contains(c.InvocationId))
                .Select(c => request.Catalog.ByCanonicalId.TryGetValue(c.CanonicalId, out var d) ? d : null)
                .Any(d => d?.Completion == CompletionPolicy.Terminal);
            if (terminalHit)
                return Finish(steps, outputs, decisionsUsed, TerminationReason.TerminalCapability, "terminal capability executed");

            // 回填结果，生成 Snapshot N+1（D7）
            try
            {
                context = await _contextUpdater.ApplyToolResultsAsync(
                    request.SessionId, request.TurnId, decisionResult.Calls, batch.Results, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Finish(steps, outputs, decisionsUsed, TerminationReason.Cancelled, "cancelled");
            }
        }

        return Finish(steps, outputs, decisionsUsed, TerminationReason.MaxDecisionsReached, $"max decisions reached: {budgets.MaxDecisions}");
    }
    private static DecisionLoopResult Finish(
        List<AgentToolUseStep> steps,
        List<OutputItem> outputs,
        int decisionsUsed,
        TerminationReason reason,
        string detail,
        string? finalText = null) => new()
    {
        Success = reason == TerminationReason.FinalReply || reason == TerminationReason.TerminalCapability,
        FinalText = finalText ?? (reason == TerminationReason.FinalReply
            ? steps.LastOrDefault(s => s.Action == "final_reply")?.Summary ?? string.Empty
            : null),
        Outputs = outputs,
        Steps = steps,
        DecisionsUsed = decisionsUsed,
        TerminationReason = reason,
        TerminationDetail = detail
    };

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max] + "...";
    }
}
