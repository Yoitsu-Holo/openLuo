using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Infrastructure;

/// <summary>
/// 默认能力调度器（D7-D10）。
/// - 兄弟节点共享同一 executionContext（同一 ReadSnapshot / MutationCollector / OutputQueue）
/// - 默认并行（≤ MaxConcurrentTools）；ParallelSafe=false 或共享资源 → 串行
/// - 非法并行批次 → 整批拒绝（D8）
/// - 本地 mutation：收集后整批校验原子提交（D9）；外部副作用允许部分成功（D10）
/// - 结果按模型调用顺序合并
/// </summary>
public sealed class DefaultCapabilityDispatcher : ICapabilityDispatcher
{
    private readonly ICapabilityInvoker _invoker;
    private readonly ICapabilityPolicy _policy;
    private readonly IStateTransaction _stateTransaction;
    private readonly IReadOnlyDictionary<string, ICapabilityInvoker> _canonicalInvokers;
    private readonly IReadOnlyDictionary<string, ICapabilityInvoker> _kindInvokers;
    private readonly openLuo.Core.Interfaces.IGameLogger? _logger;

    public DefaultCapabilityDispatcher(
        ICapabilityInvoker defaultInvoker,
        ICapabilityPolicy policy,
        IStateTransaction stateTransaction,
        IEnumerable<KeyValuePair<string, ICapabilityInvoker>>? kindInvokers = null,
        IEnumerable<KeyValuePair<string, ICapabilityInvoker>>? canonicalInvokers = null,
        openLuo.Core.Interfaces.IGameLogger? logger = null)
    {
        _invoker = defaultInvoker;
        _policy = policy;
        _stateTransaction = stateTransaction;
        _kindInvokers = kindInvokers?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ICapabilityInvoker>(StringComparer.OrdinalIgnoreCase);
        _canonicalInvokers = canonicalInvokers?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ICapabilityInvoker>(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<BatchExecutionResult> ExecuteBatchAsync(
        IReadOnlyList<CapabilityCall> calls,
        CapabilityDecisionContext context,
        CapabilityCatalogSnapshot snapshot,
        CapabilityExecutionContext executionContext,
        CancellationToken ct = default)
    {
        if (calls.Count == 0)
            return new BatchExecutionResult { Results = [] };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var validation = _policy.ValidateBatch(calls, snapshot, context);
        if (!validation.Ok)
        {
            _logger?.Warn("agent/dispatch",
                $"[tool] batch rejected: {validation.RejectionReason} (n={calls.Count})");
            return new BatchExecutionResult
            {
                Rejected = true,
                RejectionReason = validation.RejectionReason
            };
        }

        var orderedResults = new CapabilityResult?[calls.Count];
        var orderIndex = calls
            .Select((call, index) => (call, index))
            .ToDictionary(t => t.call.InvocationId, t => t.index, StringComparer.OrdinalIgnoreCase);

        var budget = context.Budgets;
        var parallelLimit = Math.Max(1, budget.MaxConcurrentTools);

        // 可并行子批次（按模型顺序切片控制并发度）
        var parallel = validation.ParallelCalls ?? [];
        for (var i = 0; i < parallel.Count; i += parallelLimit)
        {
            ct.ThrowIfCancellationRequested();
            var slice = parallel.Skip(i).Take(parallelLimit).ToList();
            var tasks = slice.Select(call => InvokeSafelyAsync(call, snapshot, executionContext, ct)).ToList();
            var results = await Task.WhenAll(tasks);
            for (var j = 0; j < slice.Count; j++)
                orderedResults[orderIndex[slice[j].InvocationId]] = results[j];
        }

        // 串行子批次（含 ParallelSafe=false 或共享资源冲突）
        if (validation.SerializedCalls is { Count: > 0 })
        {
            foreach (var call in validation.SerializedCalls)
            {
                orderedResults[orderIndex[call.InvocationId]] = await InvokeSafelyAsync(call, snapshot, executionContext, ct);
            }
        }

        var filled = orderedResults.Select(r => r ?? new CapabilityResult
        {
            InvocationId = string.Empty,
            Status = CapabilityStatus.Failed,
            Success = false,
            Error = "invoker returned no result"
        }).ToList();

        var okCount = filled.Count(r => r.Status == CapabilityStatus.Ok);
        _logger?.Info("agent/dispatch",
            $"[tool] batch done ok={okCount}/{filled.Count} ms={sw.ElapsedMilliseconds}");

        // 本地 mutation 整批提交（D9）
        MutationBatchResult? mutationOutcome = null;
        var intents = executionContext.MutationCollector.Collected;
        if (intents.Count > 0)
        {
            var subjectId = executionContext.SubjectId;
            var baseVersion = executionContext.ReadSnapshot.GetVersion(subjectId);
            mutationOutcome = await _stateTransaction.CommitAsync(subjectId, baseVersion, intents, ct);
            if (mutationOutcome.Status == MutationBatchStatus.Conflict)
            {
                // 冲突：整批不提交，回填结构化结果（由决策循环决定如何处理）
                for (var i = 0; i < filled.Count; i++)
                {
                    var r = filled[i];
                    if (!r.Success)
                        continue;
                    filled[i] = new CapabilityResult
                    {
                        InvocationId = r.InvocationId,
                        Success = r.Success,
                        Error = r.Error,
                        Status = r.Status,
                        Text = r.Text,
                        Outputs = r.Outputs,
                        Mutations = [],
                        AccessTrace = r.AccessTrace
                    };
                }
            }
        }

        return new BatchExecutionResult
        {
            Results = filled,
            MutationOutcome = mutationOutcome,
            Rejected = false
        };
    }

    private async Task<CapabilityResult> InvokeSafelyAsync(
        CapabilityCall call,
        CapabilityCatalogSnapshot snapshot,
        CapabilityExecutionContext baseContext,
        CancellationToken ct)
    {
        var descriptor = snapshot.ByCanonicalId.TryGetValue(call.CanonicalId, out var d) ? d : null;
        var invoker = _canonicalInvokers.TryGetValue(call.CanonicalId, out var canonicalInvoker)
            ? canonicalInvoker
            : descriptor is not null && _kindInvokers.TryGetValue(descriptor.Kind.ToString(), out var kindInvoker)
                ? kindInvoker
                : _invoker;

        var executionContext = new CapabilityExecutionContext
        {
            GameId = baseContext.GameId,
            SessionId = baseContext.SessionId,
            TurnId = baseContext.TurnId,
            SubjectId = baseContext.SubjectId,
            SnapshotVersion = baseContext.SnapshotVersion,
            InvocationId = call.InvocationId,
            IdempotencyKey = call.IdempotencyKey,
            DeadlineUtc = baseContext.DeadlineUtc,
            Permissions = baseContext.Permissions,
            ReadSnapshot = baseContext.ReadSnapshot,
            MutationCollector = baseContext.MutationCollector,
            OutputQueue = baseContext.OutputQueue,
            SystemBlocks = baseContext.SystemBlocks
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var provider = descriptor?.ProviderId ?? "?";
        _logger?.Info("agent/dispatch",
            $"[tool] start {call.CanonicalId} inv={call.InvocationId} provider={provider}");

        try
        {
            var result = await invoker.InvokeAsync(call, executionContext, ct);
            LogToolOutcome(call.CanonicalId, provider, result, sw.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger?.Warn("agent/dispatch",
                $"[tool] {call.CanonicalId} cancelled ({sw.ElapsedMilliseconds}ms)");
            return new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Status = CapabilityStatus.Cancelled,
                Success = false,
                Error = "cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger?.Error("agent/dispatch",
                $"[tool] {call.CanonicalId} exception ({sw.ElapsedMilliseconds}ms): {ex.Message}");
            return new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Status = CapabilityStatus.Failed,
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>按结果状态分级输出单工具调用日志（公共日志中间件 agent/dispatch category）。</summary>
    private void LogToolOutcome(string canonicalId, string provider, CapabilityResult result, long ms)
    {
        switch (result.Status)
        {
            case CapabilityStatus.Ok:
                _logger?.Info("agent/dispatch",
                    $"[tool] {canonicalId} ok ({ms}ms) provider={provider}");
                break;
            case CapabilityStatus.Cancelled:
                _logger?.Warn("agent/dispatch",
                    $"[tool] {canonicalId} cancelled ({ms}ms)");
                break;
            default:
                _logger?.Warn("agent/dispatch",
                    $"[tool] {canonicalId} {result.Status.ToString().ToLowerInvariant()} ({ms}ms) error={result.Error}");
                break;
        }
    }
}
