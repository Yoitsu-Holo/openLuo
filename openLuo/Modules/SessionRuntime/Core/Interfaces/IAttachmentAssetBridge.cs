using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

public interface IAttachmentAssetBridge
{
    Task<string?> CreateAssetForAttachmentAsync(
        SessionAttachmentPayload payload,
        string gameId,
        string sourceId,
        string channelId,
        CancellationToken ct = default);
}
