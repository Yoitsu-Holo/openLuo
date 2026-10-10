namespace openLuo.Core.Interfaces;

/// <summary>资产元数据（协议 `AssetRef`/`AssetMeta`）。</summary>
public sealed record AssetInfo(
    string Id,
    string Mime,
    long Size,
    string Checksum,
    string? SessionId,
    string? FileName,
    DateTimeOffset CreatedAt);

/// <summary>资产字节 + 元数据。</summary>
public sealed record AssetBlob(AssetInfo Info, byte[] Bytes);

/// <summary>
/// 资产存储端口（协议 §9）：二进制内容以引用（`assetId`）在协议中传递，不再内联 data URL。
/// 由宿主实现（元数据 SQLite + 磁盘字节）；内核不感知存储细节。
/// </summary>
public interface IAssetStore
{
    /// <summary>写入资产并返回元数据；超过上限抛 <see cref="InvalidOperationException"/>。</summary>
    Task<AssetInfo> PutAsync(
        ReadOnlyMemory<byte> bytes, string mime, string? sessionId = null, string? fileName = null,
        CancellationToken ct = default);

    /// <summary>按 id 读取资产（字节 + 元数据）；不存在返回 null。</summary>
    Task<AssetBlob?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>元数据查询；不存在返回 null。</summary>
    AssetInfo? Stat(string id);

    /// <summary>删除资产；返回是否命中。</summary>
    bool Delete(string id);
}
