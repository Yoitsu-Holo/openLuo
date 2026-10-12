using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>Hub 运行参数（后续由 <c>server.jsonc</c> 驱动）。</summary>
public sealed class HubServerOptions
{
    public string Listen { get; init; } = $"http://127.0.0.1:{ProtocolInfo.DefaultPort}";
    public bool AllowAnonymous { get; init; } = true;
    public string ServerVersion { get; init; } = "0.1.0";

    /// <summary>单资产字节上限（超出回 `7002 asset.too_large`）。</summary>
    public long AssetMaxBytes { get; init; } = 16L * 1024 * 1024;

    /// <summary>高危能力确认等待上限（秒）；超时视为拒绝。</summary>
    public int ConfirmTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// 入口治理策略（限流 + 输入校验，§4.9）。宿主传入**闭包**即可让 <c>security.jsonc</c> 的热加载生效；
    /// 未提供则用 <see cref="Security.HubSecurityPolicy.Default"/>（宽松但真实生效）。
    /// </summary>
    public Func<Security.HubSecurityPolicy>? Security { get; init; }
}
