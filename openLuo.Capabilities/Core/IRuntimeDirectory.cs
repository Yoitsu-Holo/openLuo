using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Core;

/// <summary>
/// 运行时目录（只读）：供协议层暴露「当前有哪些能力」（`GET /v1/capabilities`）。
/// 与 <see cref="IAgentRuntime"/> 平级的宿主服务端口；内核实现负责按会话/场景过滤。
/// </summary>
public interface IRuntimeDirectory
{
    /// <summary>列出可见能力描述。<paramref name="sessionId"/> 为空表示不按会话过滤。</summary>
    Task<IReadOnlyList<CapabilityDescriptor>> ListCapabilitiesAsync(
        string? sessionId = null, CancellationToken ct = default);
}
