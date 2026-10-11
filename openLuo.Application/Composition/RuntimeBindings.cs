using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Composition;

/// <summary>Safe fallback for a capability that has no host/extension invoker binding yet.</summary>
public sealed class UnboundCapabilityInvoker : ICapabilityInvoker
{
    public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default) =>
        Task.FromResult(new CapabilityResult
        {
            InvocationId = call.InvocationId,
            Success = false,
            Status = CapabilityStatus.Failed,
            Error = $"capability '{call.CanonicalId}' is not bound to an invoker"
        });
}
