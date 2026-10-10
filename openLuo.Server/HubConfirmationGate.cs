using openLuo.Capabilities.Core;

namespace openLuo.Server;

/// <summary>
/// <see cref="IConfirmationGate"/> 的可接入实现：Hub 启动时 <see cref="Attach"/> 投递/等待逻辑
/// （向会话订阅者推 `confirm.request` 并等待 `confirm.response`）；未接入时**默认拒绝**（安全优先）。
/// </summary>
public sealed class HubConfirmationGate : IConfirmationGate
{
    private volatile Func<ConfirmationRequest, CancellationToken, Task<bool>>? _handler;

    public void Attach(Func<ConfirmationRequest, CancellationToken, Task<bool>> handler) => _handler = handler;

    public Task<bool> RequestAsync(ConfirmationRequest request, CancellationToken ct = default) =>
        _handler is { } handler ? handler(request, ct) : Task.FromResult(false);
}
