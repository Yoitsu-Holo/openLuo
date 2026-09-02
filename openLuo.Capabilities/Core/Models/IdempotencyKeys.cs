namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// 幂等键生成（D11）。同一逻辑调用（同一 canonical id + 归一化参数 + 同一父决策）
/// 生成稳定键；重试复用同键。
/// </summary>
public static class IdempotencyKeys
{
    public static string Create(string canonicalId, string parentDecisionId, string[] args, IReadOnlyDictionary<string, string> options)
    {
        var parts = new List<string> { canonicalId, parentDecisionId };
        parts.AddRange(args ?? []);
        if (options is { Count: > 0 })
        {
            foreach (var pair in options.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                parts.Add($"{pair.Key}={pair.Value}");
        }

        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join("\u001f", parts)))).ToLowerInvariant();
    }
}

/// <summary>
/// 幂等状态跟踪：记录已见键及结果，支持重试复用与重复调用检测。
/// </summary>
public interface IIdempotencyRegistry
{
    CapabilityResult? TryGet(string idempotencyKey);
    void Record(string idempotencyKey, CapabilityResult result);
}

public sealed class InMemoryIdempotencyRegistry : IIdempotencyRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CapabilityResult> _results =
        new(StringComparer.OrdinalIgnoreCase);

    public CapabilityResult? TryGet(string idempotencyKey) =>
        _results.TryGetValue(idempotencyKey, out var result) ? result : null;

    public void Record(string idempotencyKey, CapabilityResult result) =>
        _results[idempotencyKey] = result;
}
