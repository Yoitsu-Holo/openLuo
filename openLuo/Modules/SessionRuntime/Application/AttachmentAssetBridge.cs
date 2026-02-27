using System.Text.Json;
using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class AttachmentAssetBridge : IAttachmentAssetBridge
{
    private readonly IAssetStore _assetStore;
    private readonly IAssetBlobStore _assetBlobStore;
    private readonly IAssetMetaStore _assetMetaStore;

    public AttachmentAssetBridge(
        IAssetStore assetStore,
        IAssetBlobStore assetBlobStore,
        IAssetMetaStore assetMetaStore)
    {
        _assetStore = assetStore;
        _assetBlobStore = assetBlobStore;
        _assetMetaStore = assetMetaStore;
    }

    public async Task<string?> CreateAssetForAttachmentAsync(
        SessionAttachmentPayload payload,
        string gameId,
        string sourceId,
        string channelId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        var attachment = payload.Attachment;
        var assetType = InferAssetType(attachment.MediaType);
        var assetNamespace = InferNamespace(attachment.MediaType);
        var label = string.IsNullOrWhiteSpace(attachment.Name) ? attachment.AttachmentId : attachment.Name;

        var assetId = await _assetStore.CreateAsync(new AssetRecord
        {
            GameId = gameId,
            AssetType = assetType,
            Namespace = assetNamespace,
            OwnerKind = "system",
            OwnerId = attachment.SessionId,
            Label = label,
            Status = "active",
            SourceType = "imported"
        });

        await _assetBlobStore.PutAsync(
            assetId,
            attachment.MediaType ?? "application/octet-stream",
            "primary",
            payload.Data,
            isPrimary: true);

        var metaJson = JsonSerializer.Serialize(new
        {
            sessionId = attachment.SessionId,
            attachmentId = attachment.AttachmentId,
            sourceId,
            channelId,
            originalFilePath = attachment.OriginalFilePath,
            sha256 = attachment.Sha256,
            sizeBytes = attachment.SizeBytes,
            mediaType = attachment.MediaType,
            kind = attachment.Kind.ToString()
        });
        await _assetMetaStore.PutAsync(assetId, "session_input", metaJson);

        return assetId;
    }

    private static string InferAssetType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
            return "binary";
        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return "image";
        if (mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return "audio";
        if (mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return "video";
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return "document";
        return "binary";
    }

    private static string InferNamespace(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
            return "session_attachment";
        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return "session_image";
        if (mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return "session_audio";
        if (mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return "session_video";
        return "session_attachment";
    }
}
