using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using openLuo.Core.Interfaces;
using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;

namespace openLuo.Modules.Executor.Application.RandomImage;

public sealed class RandomImageFetchExecutor(
    HttpClient httpClient,
    IAssetStore assetStore,
    IAssetBlobStore blobStore,
    IGameLogger? logger = null) : IExecutor<RandomImageFetchInput, RandomImageFetchOutput>
{
    public string Name => "random_image_fetch";

    private const string DuckMoUrl = "https://api.mossia.top/duckMo";
    private const int PixivCatMaxAttempts = 3;

    public async Task<ExecutorResult<RandomImageFetchOutput>> ExecuteAsync(RandomImageFetchInput input, CancellationToken ct = default)
    {
        string? lastError = null;
        string? sourceUrl = null;
        string? pixivArtworkUrl = null;
        string? pixivCatUrl = null;

        try
        {
            logger?.Info("executor/random_image", "random image fetch start", new
            {
                gameId = input.GameId,
                ownerCharacterId = input.OwnerCharacterId,
                endpoint = DuckMoUrl
            });

            var payload = await httpClient.GetFromJsonAsync<DuckMoResponse>(DuckMoUrl, cancellationToken: ct);
            sourceUrl = payload?.Data?.FirstOrDefault()?.UrlsList?.FirstOrDefault()?.Url;
            if (string.IsNullOrWhiteSpace(sourceUrl))
            {
                logger?.Warn("executor/random_image", "random image endpoint returned empty source url", new
                {
                    endpoint = DuckMoUrl
                });
                return ExecutorResult<RandomImageFetchOutput>.Fail("随机图片获取失败：随机图片服务未返回有效图片地址。");
            }

            logger?.Info("executor/random_image", "random image source resolved", new
            {
                endpoint = DuckMoUrl,
                sourceUrl
            });

            var artworkId = TryExtractPixivArtworkId(sourceUrl);
            pixivArtworkUrl = TryBuildPixivArtworkUrl(artworkId);
            pixivCatUrl = TryBuildPixivCatUrl(artworkId);

            logger?.Info("executor/random_image", "random image pid resolved", new
            {
                gameId = input.GameId,
                ownerCharacterId = input.OwnerCharacterId,
                sourceUrl,
                pixivCatUrl,
                pixivArtworkUrl,
                artworkId
            });

            if (!string.IsNullOrWhiteSpace(pixivCatUrl))
            {
                var pixivCatDownload = await TryDownloadImageWithRetryAsync(
                    pixivCatUrl,
                    PixivCatMaxAttempts,
                    input.GameId,
                    input.OwnerCharacterId,
                    ct);
                if (pixivCatDownload.success)
                {
                    logger?.Info("executor/random_image", "random image pixiv.cat fallback succeeded", new
                    {
                        sourceUrl,
                        pixivCatUrl
                    });
                    return await StoreDownloadedImageAsync(input, pixivCatDownload.bytes!, pixivCatDownload.mediaType!, pixivCatUrl, "Pixiv 镜像图片", ct);
                }

                lastError = pixivCatDownload.error;
                logger?.Warn("executor/random_image", "random image pixiv.cat fallback failed", new
                {
                    gameId = input.GameId,
                    ownerCharacterId = input.OwnerCharacterId,
                    sourceUrl,
                    pixivCatUrl,
                    pixivArtworkUrl,
                    error = lastError
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lastError = ex.Message;
            logger?.Warn("executor/random_image", "random image fetch failed", new
            {
                gameId = input.GameId,
                ownerCharacterId = input.OwnerCharacterId,
                error = ex.Message,
                sourceUrl,
                pixivCatUrl,
                pixivArtworkUrl
            });
        }

        if (!string.IsNullOrWhiteSpace(pixivArtworkUrl))
        {
            logger?.Warn("executor/random_image", "random image fallback artwork url returned", new
            {
                gameId = input.GameId,
                ownerCharacterId = input.OwnerCharacterId,
                sourceUrl,
                pixivCatUrl,
                fallbackArtworkUrl = pixivArtworkUrl,
                error = lastError
            });

            return ExecutorResult<RandomImageFetchOutput>.Ok(new RandomImageFetchOutput
            {
                Success = true,
                Error = lastError,
                SourceUrl = sourceUrl,
                FallbackArtworkUrl = pixivArtworkUrl,
                Label = "Pixiv作品链接"
            });
        }

        return ExecutorResult<RandomImageFetchOutput>.Fail($"随机图片获取失败：{lastError ?? "未知错误"}");
    }

    private async Task<ExecutorResult<RandomImageFetchOutput>> StoreDownloadedImageAsync(
        RandomImageFetchInput input,
        byte[] bytes,
        string mediaType,
        string sourceUrl,
        string label,
        CancellationToken ct)
    {
        var assetId = await assetStore.CreateAsync(new AssetRecord
        {
            Id = $"asset_{Guid.NewGuid():N}",
            GameId = input.GameId,
            AssetType = "image",
            Namespace = "random_image",
            OwnerKind = "character",
            OwnerId = input.OwnerCharacterId,
            Label = label,
            Status = "active",
            SourceType = "imported"
        });

        await blobStore.PutAsync(assetId, mediaType, "primary", bytes, isPrimary: true);

        logger?.Info("executor/random_image", "random image fetch stored", new
        {
            sourceUrl,
            assetId,
            mediaType,
            sizeBytes = bytes.Length
        });

        return ExecutorResult<RandomImageFetchOutput>.Ok(new RandomImageFetchOutput
        {
            Success = true,
            AssetId = assetId,
            MimeType = mediaType,
            Label = label,
            SourceUrl = sourceUrl
        });
    }

    private async Task<(bool success, byte[]? bytes, string? mediaType, string? error)> TryDownloadImageAsync(string url, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(url, ct);
        logger?.Info("executor/random_image", "random image download response", new
        {
            sourceUrl = url,
            statusCode = (int)response.StatusCode,
            reasonPhrase = response.ReasonPhrase
        });

        if (!response.IsSuccessStatusCode)
            return (false, null, null, $"下载图片失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase ?? "<none>"}");

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mediaType) || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            mediaType = "image/jpeg";

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0)
            return (false, null, null, "随机图片下载结果为空。");

        return (true, bytes, mediaType, null);
    }

    private async Task<(bool success, byte[]? bytes, string? mediaType, string? error)> TryDownloadImageWithRetryAsync(
        string url,
        int maxAttempts,
        string gameId,
        string ownerCharacterId,
        CancellationToken ct)
    {
        string? lastError = null;

        for (var attempt = 1; attempt <= Math.Max(1, maxAttempts); attempt++)
        {
            try
            {
                logger?.Info("executor/random_image", "random image download attempt", new
                {
                    gameId,
                    ownerCharacterId,
                    downloadUrl = url,
                    attempt,
                    maxAttempts
                });

                var result = await TryDownloadImageAsync(url, ct);
                if (result.success)
                    return result;

                lastError = result.error;
                logger?.Warn("executor/random_image", "random image download attempt failed", new
                {
                    gameId,
                    ownerCharacterId,
                    downloadUrl = url,
                    attempt,
                    maxAttempts,
                    error = lastError
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex.Message;
                logger?.Warn("executor/random_image", "random image download attempt exception", new
                {
                    gameId,
                    ownerCharacterId,
                    downloadUrl = url,
                    attempt,
                    maxAttempts,
                    error = ex.Message,
                    innerError = ex.InnerException?.Message
                });
            }

            if (attempt < maxAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
        }

        return (false, null, null, lastError ?? "下载图片失败。");
    }

    private static string? TryExtractPixivArtworkId(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return null;

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
            return null;

        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var match = Regex.Match(fileName, @"^(?<id>\d+)");
        if (!match.Success)
            return null;

        return match.Groups["id"].Value;
    }

    private static string? TryBuildPixivArtworkUrl(string? artworkId)
    {
        return string.IsNullOrWhiteSpace(artworkId)
            ? null
            : $"https://www.pixiv.net/artworks/{artworkId}";
    }

    private static string? TryBuildPixivCatUrl(string? artworkId)
    {
        return string.IsNullOrWhiteSpace(artworkId)
            ? null
            : $"https://pixiv.cat/{artworkId}.png";
    }

    private sealed class DuckMoResponse
    {
        [JsonPropertyName("data")]
        public List<DuckMoDataItem>? Data { get; init; }
    }

    private sealed class DuckMoDataItem
    {
        [JsonPropertyName("urlsList")]
        public List<DuckMoUrlItem>? UrlsList { get; init; }
    }

    private sealed class DuckMoUrlItem
    {
        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }
}
