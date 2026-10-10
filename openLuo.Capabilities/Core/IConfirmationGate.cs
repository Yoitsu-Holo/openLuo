namespace openLuo.Capabilities.Core;

/// <summary>高危能力确认请求（协议 §8 的 `confirm.request`）。</summary>
public sealed record ConfirmationRequest(
    string SessionId,
    string TurnId,
    string CanonicalId,
    string Risk,
    string Summary,
    string? ArgsPreview = null);

/// <summary>
/// 确认闸门端口：能力声明 <see cref="Models.CapabilityDescriptor.RequiresConfirmation"/> 时，
/// 派发前经此请求用户确认；未接入 / 超时 / 拒绝一律返回 false（**默认拒绝**，安全优先）。
/// </summary>
public interface IConfirmationGate
{
    Task<bool> RequestAsync(ConfirmationRequest request, CancellationToken ct = default);
}
