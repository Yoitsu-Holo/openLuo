using System.Text.Json.Nodes;

namespace openLuo.Capabilities.Core;

/// <summary>配置来源层名（与协议 `ConfigSources` 取值一致）。</summary>
public static class ConfigSourceNames
{
    public const string Default = "default";
    public const string File = "file";
    public const string Runtime = "runtime";
}

/// <summary>配置命名空间摘要（协议 `GET /v1/config`）。</summary>
public sealed record ConfigNamespaceInfo(
    string Namespace,
    string Source,
    bool Overridden,
    DateTimeOffset UpdatedAt);

/// <summary>配置命名空间视图：合并后的有效值 + 当前覆盖层。</summary>
public sealed record ConfigNamespaceView(
    string Namespace,
    string Source,
    JsonNode? Values,
    JsonNode? Overrides);

/// <summary>
/// 运行时配置服务端口（协议 §5.7）：有效值 = **default ⊕ file ⊕ runtime**（后者覆盖前者）。
/// 由宿主实现（磁盘 `config/{ns}.jsonc` + 进程内运行时覆盖）。内核不感知具体存储。
/// </summary>
public interface IConfigService
{
    /// <summary>列出已知命名空间。</summary>
    IReadOnlyList<ConfigNamespaceInfo> ListNamespaces();

    /// <summary>获取命名空间有效值；未知返回 null。</summary>
    Task<ConfigNamespaceView?> GetAsync(string ns, CancellationToken ct = default);

    /// <summary>以递归合并方式写入覆盖层；<paramref name="persist"/> 为真时落盘。</summary>
    Task<ConfigNamespaceView> SetAsync(string ns, JsonNode values, bool persist, CancellationToken ct = default);

    /// <summary>删除覆盖（回退到 file / default）；<paramref name="persist"/> 为真时同时删除磁盘文件。</summary>
    Task<ConfigNamespaceView?> DeleteAsync(string ns, bool persist, CancellationToken ct = default);
}
