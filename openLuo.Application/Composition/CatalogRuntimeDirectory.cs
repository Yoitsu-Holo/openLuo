using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Composition;

/// <summary>
/// <see cref="IRuntimeDirectory"/> 的默认实现：把 <see cref="ICapabilityCatalog"/> 的快照
/// 投影为能力描述列表（协议 `GET /v1/capabilities` 用）。
/// </summary>
public sealed class CatalogRuntimeDirectory : IRuntimeDirectory
{
    private readonly ICapabilityCatalog _catalog;

    public CatalogRuntimeDirectory(ICapabilityCatalog catalog) => _catalog = catalog;

    public async Task<IReadOnlyList<CapabilityDescriptor>> ListCapabilitiesAsync(
        string? sessionId = null, CancellationToken ct = default)
    {
        var snapshot = await _catalog.BuildSnapshotAsync(new CatalogBuildContext
        {
            SessionId = sessionId ?? string.Empty,
        }, ct);

        return snapshot.ByCanonicalId.Values.ToList();
    }
}
