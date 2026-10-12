using System.Collections.Concurrent;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Infrastructure;


/// <summary>
/// 内存状态事务（D20/D9）。内核保证"版本 + 意图 + 原子提交 + 冲突检测"；
/// 字段校验（mutable/clamp/maxDelta）由扩展的 mutation handler 在提交前完成。
/// 每个 SubjectId 一个状态桶，乐观锁：baseVersion 不匹配即冲突。
/// </summary>
public sealed class InMemoryStateTransaction : IStateTransaction, IStateReader
{
    private sealed class Bucket
    {
        public long Version;
        public Dictionary<string, object?> Values = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _commitGate = new();
    private readonly IStatePersistence? _persistence;

    /// <param name="persistence">可选的持久化端口：给了就启动时回载、提交后落盘（状态跨重启保留）。</param>
    public InMemoryStateTransaction(IStatePersistence? persistence = null)
    {
        _persistence = persistence;
        if (persistence is null)
            return;

        foreach (var (subjectId, version, values) in persistence.Load())
        {
            var bucket = new Bucket { Version = version };
            foreach (var pair in values)
                bucket.Values[pair.Key] = pair.Value;
            _buckets[subjectId] = bucket;
        }
    }

    /// <summary>
    /// 只读快照（供 Hub 投影状态）：加同一把提交锁，避免读到"提交进行中"的中间态；
    /// 未提交过任何变更的 subject 返回 null。
    /// </summary>
    public StateView? TryGet(string subjectId)
    {
        if (!_buckets.TryGetValue(subjectId, out var bucket))
            return null;

        lock (_commitGate)
            return new StateView(bucket.Version, new Dictionary<string, object?>(bucket.Values, StringComparer.OrdinalIgnoreCase));
    }

    public Task<MutationBatchResult> CommitAsync(
        string subjectId,
        long baseVersion,
        IReadOnlyList<MutationIntent> intents,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var bucket = _buckets.GetOrAdd(subjectId, static _ => new Bucket());

        lock (_commitGate)
        {
            if (bucket.Version != baseVersion)
            {
                return Task.FromResult(new MutationBatchResult
                {
                    Status = MutationBatchStatus.Conflict,
                    Conflicts = [.. intents.Select(i => i.ResourcePath).Distinct(StringComparer.OrdinalIgnoreCase)]
                });
            }

            // 同批内同一资源路径的多个 intent 视为冲突（兄弟节点互不感知，禁止同批双写）
            var dup = intents
                .GroupBy(i => i.ResourcePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (dup is not null)
            {
                return Task.FromResult(new MutationBatchResult
                {
                    Status = MutationBatchStatus.Conflict,
                    Conflicts = [dup.Key]
                });
            }

            foreach (var intent in intents)
                Apply(bucket, intent);

            bucket.Version++;
            // 提交即落盘（持锁内做，保证落盘内容与下方返回的快照一致；状态提交是每回合量级，写入频率可接受）
            _persistence?.Save(subjectId, bucket.Version, bucket.Values);
            return Task.FromResult(new MutationBatchResult
            {
                Status = MutationBatchStatus.Committed,
                NewSnapshot = new StateSnapshot
                {
                    SubjectId = subjectId,
                    Version = bucket.Version,
                    Values = new Dictionary<string, object?>(bucket.Values, StringComparer.OrdinalIgnoreCase)
                }
            });
        }
    }

    private static void Apply(Bucket bucket, MutationIntent intent)
    {
        switch (intent.Op)
        {
            case MutationOp.Set:
                bucket.Values[intent.ResourcePath] = intent.Value;
                break;
            case MutationOp.Remove:
                bucket.Values.Remove(intent.ResourcePath);
                break;
            case MutationOp.Add:
            case MutationOp.Increment:
                var current = bucket.Values.TryGetValue(intent.ResourcePath, out var existing) ? existing : null;
                bucket.Values[intent.ResourcePath] = current switch
                {
                    decimal d when intent.Value is decimal dv => d + dv,
                    long l when intent.Value is long lv => l + lv,
                    int i when intent.Value is int iv => i + iv,
                    _ => intent.Value
                };
                break;
        }
    }
}
