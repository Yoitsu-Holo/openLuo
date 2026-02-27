using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

public interface IInputContentStore
{
    Task<SessionAttachment> PutAsync(string sessionId, SessionInputPart part, CancellationToken ct = default);

    Task<SessionAttachmentPayload?> GetAsync(string sessionId, string attachmentId, CancellationToken ct = default);

    Task<IReadOnlyList<SessionAttachment>> ListAsync(string sessionId, CancellationToken ct = default);

    Task<SessionAttachment?> LinkAssetAsync(string sessionId, string attachmentId, string assetId, CancellationToken ct = default);
}
