using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Core;

namespace openLuo.Capabilities.Infrastructure;

/// <summary>
/// 能力目录默认实现（D14/D29）。初始化收集各 ICapabilitySource 的基础注册并缓存；
/// 每轮根据权限/场景/provider 健康生成不可变快照，并生成 CanonicalId ↔ ModelToolName 双向映射。
/// </summary>
public sealed class DefaultCapabilityCatalog : ICapabilityCatalog
{
    private readonly IReadOnlyList<ICapabilitySource> _sources;
    private IReadOnlyDictionary<string, CapabilityDescriptor>? _base;
    private long _snapshotVersion;

    public DefaultCapabilityCatalog(IEnumerable<ICapabilitySource> sources)
    {
        _sources = sources.ToList();
    }

    /// <summary>收集基础注册（宿主启动时调用一次，D14）。</summary>
    public void LoadBase()
    {
        var descriptors = new List<CapabilityDescriptor>();
        foreach (var source in _sources)
        {
            foreach (var descriptor in source.ListCapabilities())
            {
                if (string.IsNullOrWhiteSpace(descriptor.CanonicalId))
                    continue;
                descriptors.Add(descriptor);
            }
        }

        _base = descriptors
            .GroupBy(d => d.CanonicalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public Task<CapabilityCatalogSnapshot> BuildSnapshotAsync(
        CatalogBuildContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // 每次构建重新收集基础注册：扩展注册表可能晚于 catalog 首建（组合根循环依赖），
        // 且支持扩展热加载后目录自动刷新。
        LoadBase();
        var baseMap = _base ?? throw new InvalidOperationException(
            "DefaultCapabilityCatalog.LoadBase() must be called before building snapshots.");

        var allowed = context.Permissions.AllowedCanonicalIds;
        var allowedKinds = context.Permissions.AllowedKinds;
        var selected = baseMap.Values
            .Where(d =>
                (allowed.Count == 0 || allowed.Contains(d.CanonicalId)) &&
                allowedKinds.Contains(d.Kind.ToString()) &&
                IsProviderHealthy(d, context.ProviderHealth))
            .ToList();

        var byCanonicalId = new Dictionary<string, CapabilityDescriptor>(StringComparer.OrdinalIgnoreCase);
        var modelToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var canonicalToModel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in selected)
        {
            var modelName = GenerateModelToolName(descriptor.CanonicalId, byCanonicalId.Count);
            var mapped = descriptor with { ModelToolName = modelName };

            byCanonicalId[mapped.CanonicalId] = mapped;
            modelToCanonical[modelName] = mapped.CanonicalId;
            canonicalToModel[mapped.CanonicalId] = modelName;
        }

        return Task.FromResult(new CapabilityCatalogSnapshot
        {
            Version = ++_snapshotVersion,
            ByCanonicalId = byCanonicalId,
            ModelNameToCanonicalId = modelToCanonical,
            CanonicalIdToModelName = canonicalToModel
        });
    }

    private static bool IsProviderHealthy(
        CapabilityDescriptor descriptor,
        IReadOnlyDictionary<string, bool> providerHealth) =>
        providerHealth.Count == 0 ||
        !providerHealth.TryGetValue(descriptor.ProviderId, out var healthy) ||
        healthy;

    /// <summary>
    /// 生成模型调用名：canonical id 的确定性短哈希 + 前缀，避免模型命名限制与碰撞。
    /// 每轮快照内映射固定（D29）。
    /// </summary>
    private static string GenerateModelToolName(string canonicalId, int index)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalId));
        var suffix = Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
        var safe = new string(canonicalId
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            .ToArray());
        return $"cap_{safe}_{suffix}";
    }
}
