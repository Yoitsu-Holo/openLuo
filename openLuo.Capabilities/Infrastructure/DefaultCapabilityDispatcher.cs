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
    private readonly IConfirmationGate? _confirmationGate;

    public DefaultCapabilityDispatcher(
        ICapabilityInvoker defaultInvoker,
        ICapabilityPolicy policy,
        IStateTransaction stateTransaction,
        IEnumerable<KeyValuePair<string, ICapabilityInvoker>>? kindInvokers = null,
        IEnumerable<KeyValuePair<string, ICapabilityInvoker>>? canonicalInvokers = null,
        openLuo.Core.Interfaces.IGameLogger? logger = null,
        IConfirmationGate? confirmationGate = null)
    {
        _invoker = defaultInvoker;
        _policy = policy;
        _stateTransaction = stateTransaction;
        _kindInvokers = kindInvokers?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ICapabilityInvoker>(StringComparer.OrdinalIgnoreCase);
        _canonicalInvokers = canonicalInvokers?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ICapabilityInvoker>(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _confirmationGate = confirmationGate;
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
        var toolMaxRetries = Math.Max(0, budget.MaxToolRetries);

        // 可并行子批次（按模型顺序切片控制并发度）
        var parallel = validation.ParallelCalls ?? [];
        for (var i = 0; i < parallel.Count; i += parallelLimit)
        {
            ct.ThrowIfCancellationRequested();
            var slice = parallel.Skip(i).Take(parallelLimit).ToList();
            var tasks = slice.Select(call => InvokeSafelyAsync(call, snapshot, executionContext, ct, toolMaxRetries)).ToList();
            var results = await Task.WhenAll(tasks);
            for (var j = 0; j < slice.Count; j++)
                orderedResults[orderIndex[slice[j].InvocationId]] = results[j];
        }

        // 串行子批次（含 ParallelSafe=false 或共享资源冲突）
        if (validation.SerializedCalls is { Count: > 0 })
        {
            foreach (var call in validation.SerializedCalls)
            {
                orderedResults[orderIndex[call.InvocationId]] = await InvokeSafelyAsync(call, snapshot, executionContext, ct, toolMaxRetries);
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
        CancellationToken ct,
        int maxRetries)
    {
        var descriptor = snapshot.ByCanonicalId.TryGetValue(call.CanonicalId, out var d) ? d : null;

        // 高危能力：派发前请求确认（未接入/超时/拒绝 → 默认拒绝，安全优先）
        if (_confirmationGate is not null && descriptor is { RequiresConfirmation: true })
        {
            var approved = await _confirmationGate.RequestAsync(new ConfirmationRequest(
                SessionId: baseContext.SessionId,
                TurnId: baseContext.TurnId,
                CanonicalId: call.CanonicalId,
                Risk: descriptor.Risk.ToString().ToLowerInvariant(),
                Summary: descriptor.Summary,
                ArgsPreview: call.RawArgumentsJson), ct).ConfigureAwait(false);

            if (!approved)
            {
                return new CapabilityResult
                {
                    InvocationId = call.InvocationId,
                    Success = false,
                    Status = CapabilityStatus.Rejected,
                    Error = "confirmation denied",
                };
            }
        }

        var invoker = _canonicalInvokers.TryGetValue(call.CanonicalId, out var canonicalInvoker)
            ? canonicalInvoker
            : descriptor is not null && _kindInvokers.TryGetValue(descriptor.Kind.ToString(), out var kindInvoker)
                ? kindInvoker
                : _invoker;

        // 工具级失败重试：同一调用（InvocationId 不变）连续失败时，在 dispatcher 内
        // 原封不动重放（工程化重试，不经过模型决策、不消耗 MaxDecisions）。成功后即停止，
        // 只有连续失败累计达 MaxToolRetries 才把失败结果返回给模型。这与"多轮调用每张都
        // 成功"的场景（如连续获取 10 张图）完全兼容——每次成功都不会触发重试。
        maxRetries = Math.Max(0, maxRetries);
        var attempt = Math.Max(0, call.Attempt);
        while (true)
        {
            var executionContext = BuildExecutionContext(call, baseContext);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var provider = descriptor?.ProviderId ?? "?";
            _logger?.Info("agent/dispatch",
                $"[tool] start {call.CanonicalId} inv={call.InvocationId} attempt={attempt} provider={provider}");

            CapabilityResult result;
            try
            {
                result = await invoker.InvokeAsync(call, executionContext, ct);
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
                result = new CapabilityResult
                {
                    InvocationId = call.InvocationId,
                    Status = CapabilityStatus.Failed,
                    Success = false,
                    Error = ex.Message
                };
            }

            LogToolOutcome(call.CanonicalId, provider, result, sw.ElapsedMilliseconds);

            // 成功/取消/校验拒绝即返回：成功重置计数；Rejected 是参数/业务校验失败
            // （确定性错误，如缺 id），重放同样参数只会重复失败——立即回填让模型纠错，
            // 不为"多轮逐个成功"触发重试。只有 Failed（执行期瞬时错误）才进入重试。
            if (result.Status is CapabilityStatus.Ok
                or CapabilityStatus.Cancelled
                or CapabilityStatus.Rejected)
                return result;

            // 连续失败：attempt 从 0 起，允许最多 maxRetries 次重放（attempt 已用重放次数）。
            if (attempt >= maxRetries)
                return result;

            attempt++;
            _logger?.Warn("agent/dispatch",
                $"[tool] {call.CanonicalId} failed ({sw.ElapsedMilliseconds}ms), retrying {attempt}/{maxRetries} in-dispatch");
        }
    }

    private static CapabilityExecutionContext BuildExecutionContext(
        CapabilityCall call,
        CapabilityExecutionContext baseContext)
    {
        return new CapabilityExecutionContext
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
