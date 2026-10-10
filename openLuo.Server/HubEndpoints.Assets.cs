using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json.Nodes;
using openLuo.Core.Interfaces;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：资产，§5.10/§9）。</summary>
public static partial class HubServer
{
    /// <summary>资产域：上传 / 下载 / 元数据 / 删除。依赖 <see cref="IAssetStore"/>，未接入则整组不注册。</summary>
    private static void MapAssetEndpoints(WebApplication app, HubContext hub)
    {
        var assets = hub.Assets;
        if (assets is null)
            return;

        var options = hub.Options;
        var auditSubscribers = hub.AuditSubscribers;
        string? Actor(HttpContext ctx) => hub.Actor(ctx);

        app.MapPost("/v1/assets", async (HttpContext ctx, string? sessionId, string? fileName, CancellationToken requestCt) =>
        {
            var mime = string.IsNullOrWhiteSpace(ctx.Request.ContentType)
                ? "application/octet-stream"
                : ctx.Request.ContentType;

            using var buffer = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(buffer, requestCt);
            var bytes = buffer.ToArray();

            if (bytes.Length == 0)
                return Error(ErrorCodes.AssetInvalid, "asset is empty");
            if (bytes.Length > options.AssetMaxBytes)
                return Error(ErrorCodes.AssetTooLarge, $"asset exceeds limit: {bytes.Length} > {options.AssetMaxBytes}");

            var headerName = ctx.Request.Headers["X-File-Name"].ToString();
            try
            {
                var info = await assets.PutAsync(bytes, mime, sessionId,
                    fileName ?? (string.IsNullOrWhiteSpace(headerName) ? null : headerName), requestCt);

                await AuditAsync(auditSubscribers, "asset.upload", Actor(ctx), info.Id, "ok",
                    new JsonObject { ["mime"] = info.Mime, ["size"] = info.Size }, requestCt);

                return Json(EnvelopeFactory.Create("asset", new UploadAssetResponse
                {
                    Asset = ToAssetRef(info),
                    Meta = ToAssetMeta(info),
                }));
            }
            catch (InvalidOperationException ex)
            {
                return Error(ErrorCodes.AssetTooLarge, ex.Message);
            }
        });

        app.MapGet("/v1/assets/{id}", async (string id, HttpContext ctx, CancellationToken requestCt) =>
        {
            var blob = await assets.GetAsync(id, requestCt);
            if (blob is null)
                return Error(ErrorCodes.AssetNotFound, $"asset not found: {id}");

            if (CheckAssetAccess(ctx, blob.Info) is { } denied)
                return denied;

            return Results.File(blob.Bytes, blob.Info.Mime);
        });

        app.MapMethods("/v1/assets/{id}", ["HEAD"], (string id, HttpContext ctx) =>
        {
            var info = assets.Stat(id);
            if (info is null)
                return Error(ErrorCodes.AssetNotFound, $"asset not found: {id}");
            return CheckAssetAccess(ctx, info) ?? Json(EnvelopeFactory.Create("asset", ToAssetMeta(info)));
        });

        app.MapDelete("/v1/assets/{id}", async (string id, HttpContext ctx) =>
        {
            var info = assets.Stat(id);
            if (info is not null && CheckAssetAccess(ctx, info) is { } denied)
                return denied;
            var deleted = assets.Delete(id);
            await AuditAsync(auditSubscribers, "asset.delete", Actor(ctx), id, deleted ? "ok" : "not_found", null, default);
            return deleted
                ? Json(EnvelopeFactory.Create("asset", new { deleted = true, id }))
                : Error(ErrorCodes.AssetNotFound, $"asset not found: {id}");
        });
    }
}
