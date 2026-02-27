namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// 能力目录构建上下文（D14）：权限、场景、provider 健康状态、预算等筛选条件。
/// </summary>
public sealed class CatalogBuildContext
{
    public string SessionId { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public CapabilityPermissions Permissions { get; init; } = new();
    public string? SceneTag { get; init; }
    public DecisionBudgets Budgets { get; init; } = DecisionBudgets.Default;
    public IReadOnlyDictionary<string, bool> ProviderHealth { get; init; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 能力目录快照（D14/D29）。每轮不可变；CanonicalId ↔ ModelToolName 双向映射固定在本快照内。
/// </summary>
public sealed class CapabilityCatalogSnapshot
{
    public long Version { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, CapabilityDescriptor> ByCanonicalId { get; init; } =
        new Dictionary<string, CapabilityDescriptor>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> ModelNameToCanonicalId { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> CanonicalIdToModelName { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public CapabilityDescriptor? TryGetByModelName(string modelToolName) =>
        ModelNameToCanonicalId.TryGetValue(modelToolName, out var canonicalId)
            && ByCanonicalId.TryGetValue(canonicalId, out var descriptor)
            ? descriptor
            : null;
}

/// <summary>
/// 能力目录：初始化收集基础注册并缓存，每轮生成不可变快照（D14）。
/// </summary>
public interface ICapabilityCatalog
{
    Task<CapabilityCatalogSnapshot> BuildSnapshotAsync(
        CatalogBuildContext context,
        CancellationToken ct = default);
}
