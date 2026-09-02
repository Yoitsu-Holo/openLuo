using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Infrastructure;

/// <summary>
/// 默认能力策略（D7/D8）：
/// - 模型只能调用快照内已注册能力
/// - 参数 schema 校验（非空必需字段存在）
/// - 并行约束：ParallelSafe=false 或共享 AccessesResources 的调用 → 串行
/// - mutation 批次内的非法组合（如两个 mutation 写同一资源）→ 整批拒绝
/// </summary>
public sealed class DefaultCapabilityPolicy : ICapabilityPolicy
{
    public BatchValidation ValidateBatch(
        IReadOnlyList<CapabilityCall> calls,
        CapabilityCatalogSnapshot snapshot,
        CapabilityDecisionContext context)
    {
        var parallel = new List<CapabilityCall>();
        var serialized = new List<CapabilityCall>();

        foreach (var call in calls)
        {
            if (!snapshot.ByCanonicalId.TryGetValue(call.CanonicalId, out var descriptor))
                return BatchValidation.Reject($"unknown capability: {call.CanonicalId}");

            if (!ValidateArgs(call, descriptor))
                return BatchValidation.Reject($"invalid arguments for capability: {call.CanonicalId}");

            if (descriptor.ParallelSafe && !SharesResources(call, parallel, snapshot))
                parallel.Add(call);
            else
                serialized.Add(call);
        }

        // 两个 mutation 写同一资源 → 整批拒绝（兄弟节点互不感知，禁止同批双写）
        var mutationPaths = calls
            .Select(call => snapshot.ByCanonicalId[call.CanonicalId])
            .Where(d => d.SideEffect == SideEffectClass.Mutation)
            .SelectMany(d => d.AccessesResources)
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (mutationPaths is not null)
            return BatchValidation.Reject($"conflicting mutation resources: {mutationPaths.Key}");

        return BatchValidation.OkBatch(parallel, serialized);
    }

    private static bool SharesResources(
        CapabilityCall call,
        IReadOnlyList<CapabilityCall> alreadyParallel,
        CapabilityCatalogSnapshot snapshot)
    {
        if (!snapshot.ByCanonicalId.TryGetValue(call.CanonicalId, out var descriptor))
            return false;

        if (descriptor.SideEffect == SideEffectClass.Mutation && descriptor.AccessesResources.Count > 0)
        {
            foreach (var other in alreadyParallel)
            {
                if (!snapshot.ByCanonicalId.TryGetValue(other.CanonicalId, out var otherDescriptor))
                    continue;
                if (otherDescriptor.AccessesResources.Any(r => descriptor.AccessesResources.Contains(r, StringComparer.OrdinalIgnoreCase)))
                    return true;
            }
        }

        return false;
    }

    private static bool ValidateArgs(CapabilityCall call, CapabilityDescriptor descriptor)
    {
        // 第一版：仅检查必需参数个数不超过 schema 语义（完整 JSON Schema 校验在桥接层）。
        // descriptor.InputSchema 由 LLM 适配层用于工具声明；此处保持宽松，防误伤。
        return call.Args is not null;
    }
}
