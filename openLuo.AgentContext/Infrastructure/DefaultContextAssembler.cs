using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;

namespace openLuo.AgentContext.Infrastructure;

/// <summary>
/// 默认上下文组装器（D42/D44）：串行调用 Contributor、合并贡献、按 Region/Priority 排序、
/// 合并 SourceStates（Unavailable 保留结构化状态）、**保底 + 贪心填充**式预算裁剪、
/// 生成不可变快照 + 预算账目。Contributor 之间禁止依赖，只读同一个 ContextBuildRequest。
/// <para>
/// 裁剪规则（D44）：
/// <list type="number">
/// <item>保底区域（<see cref="ContextRegionPolicies"/>）全部纳入，即使已超上限——宁可超支也**不无声丢弃**；</item>
/// <item>可裁区域按 Priority 降序贪心填充，**跳过**放不下的条目并继续尝试更小的（不再级联 break）；</item>
/// <item>被裁条目逐条记入 <see cref="ContextBudgetSnapshot.DroppedContributions"/>，
/// 保底集超支时置 <see cref="ContextBudgetSnapshot.OverBudget"/>。</item>
/// </list>
/// 呈现顺序仍是 <see cref="ContextRegion"/> 枚举序 + 区域内 Priority 降序，保证系统块前缀稳定（利于模型侧前缀缓存）。
/// </para>
/// </summary>
public sealed class DefaultContextAssembler : IContextAssembler, IContextGlobalCache
{
    private readonly IReadOnlyList<IContextContributor> _contributors;
    private readonly ITokenCounter _counter;
    private readonly IContextLimitsProvider _limits;

    /// <summary>
    /// 全局块缓存（D46）：键 = (subjectId, contributorId)，值 = 产出 + 生成时刻。
    /// 只缓存 <see cref="ContextScope.Global"/> 且 <see cref="IContextContributor.CacheTtl"/> &gt; 0 的贡献者——
    /// 于是同一角色的所有会话**引用同一批不可变贡献**（不复制），改 persona 只需等 TTL 或显式失效。
    /// 跨 subject 不共享（不同角色的人格完全不同）。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string SubjectId, string ContributorId), (DateTimeOffset At, string? Version, ContextContributionResult Result)> _globalCache = new();

    public DefaultContextAssembler(
        IEnumerable<IContextContributor> contributors,
        ITokenCounter? counter = null,
        IContextLimitsProvider? limits = null)
    {
        _contributors = contributors.ToList();
        _counter = counter ?? HeuristicTokenCounter.Instance;
        _limits = limits ?? StaticContextLimitsProvider.Default;
    }

    /// <inheritdoc />
    public void Invalidate(string? subjectId = null)
    {
        if (subjectId is null)
        {
            _globalCache.Clear();
            return;
        }

        foreach (var key in _globalCache.Keys.Where(k => string.Equals(k.SubjectId, subjectId, StringComparison.Ordinal)))
            _globalCache.TryRemove(key, out _);
    }

    public async Task<AgentDecisionContext> BuildAsync(
        ContextBuildRequest request,
        CancellationToken ct = default)
    {
        var contributions = new List<ContextContribution>();
        var sourceStates = new List<ContextSourceState>();
        var cacheHits = 0;
        var cacheMisses = 0;

        foreach (var contributor in _contributors)
        {
            ct.ThrowIfCancellationRequested();

            var cacheKey = (request.SubjectId, contributor.Id);
            var cacheable = contributor.Scope == ContextScope.Global && contributor.CacheTtl > TimeSpan.Zero;

            // 版本探针（廉价）：有探针就**按内容**判缓存，没探针才退回 TTL。
            // 探针本身可能失败（目录不可读等）→ 返回 null，此时按 TTL 处理，不影响正确性。
            string? version = null;
            if (cacheable)
            {
                try
                {
                    version = contributor.CurrentVersion(request);
                }
                catch
                {
                    version = null;
                }
            }

            if (cacheable && _globalCache.TryGetValue(cacheKey, out var cached))
            {
                var versionMatch = version is not null && string.Equals(cached.Version, version, StringComparison.Ordinal);
                var ttlMatch = version is null && DateTimeOffset.UtcNow - cached.At < contributor.CacheTtl;
                if (versionMatch || ttlMatch)
                {
                    // 命中：复用同一批不可变贡献（引用而非复制），不调用贡献者；刷新时间戳以延长兜底 TTL
                    _globalCache[cacheKey] = (DateTimeOffset.UtcNow, cached.Version, cached.Result);
                    sourceStates.Add(cached.Result.State);
                    contributions.AddRange(cached.Result.Contributions);
                    cacheHits++;
                    continue;
                }
            }

            try
            {
                var result = await contributor.ContributeAsync(request, ct);
                sourceStates.Add(result.State);
                contributions.AddRange(result.Contributions);

                if (cacheable)
                    _globalCache[cacheKey] = (DateTimeOffset.UtcNow, version, result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单个 Contributor 失败独立降级（D42）
                sourceStates.Add(new ContextSourceState
                {
                    SourceId = contributor.Id,
                    Status = ContextSourceStatus.Unavailable,
                    Reason = ex.Message,
                    Retryable = true
                });
            }

            if (cacheable)
                cacheMisses++;
        }

        var maxTokens = _limits.Current.MaxTokens;
        var (withinBudget, budget) = Trim(contributions, maxTokens);

        return new AgentDecisionContext
        {
            SessionId = request.SessionId,
            TurnId = request.TurnId,
            SnapshotVersion = 1,
            Contributions = withinBudget,
            SourceStates = sourceStates,
            UserInput = request.UserInput,
            UserBlocks = request.UserBlocks,
            Budget = budget,
            Cache = new ContextCacheStats(cacheHits, cacheMisses)
        };
    }

    /// <summary>保底 + 贪心填充式裁剪；返回纳入的贡献（按呈现序）与预算账目。</summary>
    private (List<ContextContribution> Kept, ContextBudgetSnapshot Budget) Trim(
        List<ContextContribution> contributions, int maxTokens)
    {
        var measured = contributions
            .Select(c => (Contribution: c, Tokens: _counter.Count(c.Content)))
            .ToList();

        var pinned = measured
            .Where(m => ContextRegionPolicies.RetentionOf(m.Contribution.Region) == ContextRetention.Pinned)
            .ToList();
        var droppable = measured
            .Where(m => ContextRegionPolicies.RetentionOf(m.Contribution.Region) == ContextRetention.Droppable)
            .ToList();

        var used = pinned.Sum(m => m.Tokens);
        var overBudget = used > maxTokens;

        var kept = new List<(ContextContribution Contribution, int Tokens)>(pinned);
        var dropped = new List<DroppedContribution>();

        // 可裁区按优先级降序；同级用区域序 + id 保证确定性。放不下就跳过，继续试后面的小条目。
        foreach (var candidate in droppable
            .OrderByDescending(m => m.Contribution.Priority)
            .ThenBy(m => m.Contribution.Region)
            .ThenBy(m => m.Contribution.Id, StringComparer.Ordinal))
        {
            if (overBudget || used + candidate.Tokens > maxTokens)
            {
                dropped.Add(new DroppedContribution(
                    candidate.Contribution.Id,
                    candidate.Contribution.ContributorId,
                    candidate.Contribution.Region,
                    candidate.Contribution.Priority,
                    candidate.Tokens));
                continue;
            }

            kept.Add(candidate);
            used += candidate.Tokens;
        }

        // 呈现序：区域枚举序 → 区域内 Priority 降序 → id（稳定前缀）。
        var ordered = kept
            .OrderBy(m => m.Contribution.Region)
            .ThenByDescending(m => m.Contribution.Priority)
            .ThenBy(m => m.Contribution.Id, StringComparer.Ordinal)
            .Select(m => m.Contribution)
            .ToList();

        var budget = new ContextBudgetSnapshot
        {
            MaxTokens = maxTokens,
            SystemTokens = used,
            DroppedContributions = dropped,
            OverBudget = overBudget
        };

        return (ordered, budget);
    }
}
