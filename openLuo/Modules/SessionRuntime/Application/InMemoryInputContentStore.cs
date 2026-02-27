using System.Collections.Concurrent;
using System.Security.Cryptography;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class InMemoryInputContentStore : IInputContentStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SessionAttachmentPayload>> _sessions
        = new(StringComparer.OrdinalIgnoreCase);

    public async Task<SessionAttachment> PutAsync(string sessionId, SessionInputPart part, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(part);

        var payload = await MaterializePayloadAsync(sessionId, part, ct);
        var sessionStore = _sessions.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, SessionAttachmentPayload>(StringComparer.OrdinalIgnoreCase));
        sessionStore[payload.Attachment.AttachmentId] = payload;
        return payload.Attachment;
    }

    public Task<SessionAttachmentPayload?> GetAsync(string sessionId, string attachmentId, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var sessionStore) &&
            sessionStore.TryGetValue(attachmentId, out var payload))
        {
            return Task.FromResult<SessionAttachmentPayload?>(payload);
        }

        return Task.FromResult<SessionAttachmentPayload?>(null);
    }

    public Task<IReadOnlyList<SessionAttachment>> ListAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var sessionStore))
            return Task.FromResult<IReadOnlyList<SessionAttachment>>([]);

        IReadOnlyList<SessionAttachment> list = sessionStore.Values
            .Select(x => x.Attachment)
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();
        return Task.FromResult(list);
    }

    public Task<SessionAttachment?> LinkAssetAsync(string sessionId, string attachmentId, string assetId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var sessionStore) ||
            !sessionStore.TryGetValue(attachmentId, out var payload))
        {
            return Task.FromResult<SessionAttachment?>(null);
        }

        var updated = new SessionAttachment
        {
            AttachmentId = payload.Attachment.AttachmentId,
            SessionId = payload.Attachment.SessionId,
            Kind = payload.Attachment.Kind,
            Name = payload.Attachment.Name,
            MediaType = payload.Attachment.MediaType,
            OriginalFilePath = payload.Attachment.OriginalFilePath,
            AssetId = assetId,
            SizeBytes = payload.Attachment.SizeBytes,
            Sha256 = payload.Attachment.Sha256,
            CreatedAtUtc = payload.Attachment.CreatedAtUtc
        };
        sessionStore[attachmentId] = new SessionAttachmentPayload
        {
            Attachment = updated,
            Data = payload.Data
        };
        return Task.FromResult<SessionAttachment?>(updated);
    }

    private static async Task<SessionAttachmentPayload> MaterializePayloadAsync(
        string sessionId,
        SessionInputPart part,
        CancellationToken ct)
    {
        if (part.Kind == SessionContentKind.Text)
            throw new InvalidOperationException("Text parts are not stored in input content store.");

        byte[] data;
        string? originalFilePath = null;

        if (part.Kind == SessionContentKind.Binary)
        {
            data = part.Data ?? [];
        }
        else
        {
            if (string.IsNullOrWhiteSpace(part.FilePath))
                throw new InvalidOperationException("FileReference part requires FilePath.");

            originalFilePath = Path.GetFullPath(part.FilePath);
            data = await File.ReadAllBytesAsync(originalFilePath, ct);
        }

        var attachmentId = Guid.NewGuid().ToString("N");
        var sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var attachment = new SessionAttachment
        {
            AttachmentId = attachmentId,
            SessionId = sessionId,
            Kind = part.Kind,
            Name = part.Name,
            MediaType = part.MediaType,
            OriginalFilePath = originalFilePath,
            SizeBytes = data.LongLength,
            Sha256 = sha256
        };

        return new SessionAttachmentPayload
        {
            Attachment = attachment,
            Data = data
        };
    }
}
