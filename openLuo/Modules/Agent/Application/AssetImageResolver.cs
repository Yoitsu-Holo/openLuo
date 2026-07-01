using openLuo.Core.Models;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Assets.Core.Interfaces;

namespace openLuo.Modules.Agent.Application;

/// <summary>
/// Resolves asset:// references in ImageBlocks to data URIs by fetching blob data from the asset store.
/// Enforces image size, MIME type, and base64 prompt length limits from SecurityRuntimeConfig.
/// </summary>
public sealed class AssetImageResolver : IAssetImageResolver
{
    private readonly IAssetBlobStore _blobStore;
    private readonly IRuntimeConfigCenter _configCenter;

    public AssetImageResolver(IAssetBlobStore blobStore, IRuntimeConfigCenter configCenter)
    {
        _blobStore = blobStore;
        _configCenter = configCenter;
    }

    public async Task<IReadOnlyList<Block>> ResolveAsync(IReadOnlyList<Block>? blocks, CancellationToken ct = default)
    {
        if (blocks is not { Count: > 0 })
            return [];

        var config = _configCenter.GetSnapshot().Security;
        var allowedMimeTypes = ParseMimeWhitelist(config.AllowedImageMimeTypes);
        var maxSizeBytes = Math.Max(1024, config.MaxImageSizeBytes);
        var maxBase64Length = Math.Max(16384, config.MaxBase64PromptLength);

        var resolved = new List<Block>(blocks.Count);
        foreach (var block in blocks)
        {
            if (block is not ImageBlock image)
            {
                resolved.Add(block);
                continue;
            }

            // Enforce MIME whitelist
            if (allowedMimeTypes is { Count: > 0 } && !string.IsNullOrWhiteSpace(image.MimeType))
            {
                if (!allowedMimeTypes.Contains(image.MimeType, StringComparer.OrdinalIgnoreCase))
                {
                    resolved.Add(new TextBlock
                    {
                        Kind = BlockKind.Text,
                        Text = $"[图片类型不支持: {image.MimeType}]"
                    });
                    continue;
                }
            }

            // Already has a DataUri -- no resolution needed
            if (!string.IsNullOrWhiteSpace(image.DataUri))
            {
                resolved.Add(image);
                continue;
            }

            // If AssetId is already a URL or data URI, use it directly as DataUri
            if (image.AssetId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                image.AssetId.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                image.AssetId.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                resolved.Add(new ImageBlock
                {
                    Kind = BlockKind.Image,
                    AssetId = image.AssetId,
                    MimeType = image.MimeType,
                    Name = image.Name,
                    AltText = image.AltText,
                    Caption = image.Caption,
                    RenderHint = image.RenderHint,
                    DataUri = image.AssetId
                });
                continue;
            }

            // Resolve internal asset with size limits
            var dataUri = await ResolveAssetToDataUriAsync(image.AssetId, image.MimeType, maxSizeBytes, maxBase64Length, ct);
            resolved.Add(new ImageBlock
            {
                Kind = BlockKind.Image,
                AssetId = image.AssetId,
                MimeType = image.MimeType,
                Name = image.Name,
                AltText = image.AltText ?? (dataUri is null ? "[图片过大或无法解析]" : null),
                Caption = image.Caption,
                RenderHint = image.RenderHint,
                DataUri = dataUri
            });
        }

        return resolved;
    }

    private async Task<string?> ResolveAssetToDataUriAsync(
        string assetId,
        string mimeType,
        int maxSizeBytes,
        int maxBase64Length,
        CancellationToken ct)
    {
        try
        {
            var infos = await _blobStore.GetInfoByAssetIdAsync(assetId);
            if (infos is not { Count: > 0 })
                return null;

            var primary = infos.FirstOrDefault(b => b.IsPrimary)
                          ?? infos.First();

            // Enforce blob size limit
            if (primary.SizeBytes > maxSizeBytes)
                return null;

            var data = await _blobStore.GetDataAsync(primary.Id);
            if (data is null or { Length: 0 })
                return null;

            // Double-check actual data size against byte limit
            if (data.Length > maxSizeBytes)
                return null;

            var format = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType;
            var dataUri = $"data:{format};base64,{Convert.ToBase64String(data)}";

            // Enforce base64 prompt length limit
            if (dataUri.Length > maxBase64Length)
                return null;

            return dataUri;
        }
        catch
        {
            return null;
        }
    }

    private static HashSet<string>? ParseMimeWhitelist(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
