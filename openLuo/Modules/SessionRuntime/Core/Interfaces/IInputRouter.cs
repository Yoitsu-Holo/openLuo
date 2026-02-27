using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

public interface IInputRouter
{
    Task<SessionExecutionRequest?> RouteAsync(
        SessionInput input,
        IReadOnlyList<SessionAttachmentReference> attachments,
        CancellationToken ct = default);
}
