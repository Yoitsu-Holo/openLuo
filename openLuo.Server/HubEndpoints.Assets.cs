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

        // REST：`POST /v1/assets`（无会话归属）与 `POST /v1/sessions/{id}/assets`（父资源子集合）共用同一处理器；
        // 文件名走标准 `Content-Disposition` 头（早期的 `?fileName=` 查询参数与 `X-File-Name` 自定义头已废弃）。
        app.MapPost(HttpPaths.Assets, (HttpContext ctx, CancellationToken requestCt) =>
            UploadAsync(ctx, sessionId: null, requestCt));

        app.MapPost(HttpPaths.SessionAssetsTemplate, (string id, HttpContext ctx, CancellationToken requestCt) =>
            UploadAsync(ctx, sessionId: id, requestCt));

        async Task<IResult> UploadAsync(HttpContext ctx, string? sessionId, CancellationToken requestCt)
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

            try
            {
                var info = await assets.PutAsync(bytes, mime, sessionId, FileNameOf(ctx.Request), requestCt);

                await AuditAsync(auditSubscribers, "asset.upload", Actor(ctx), info.Id, "ok",
                    new JsonObject { ["mime"] = info.Mime, ["size"] = info.Size }, requestCt);

                return Created(ctx, HttpPaths.Asset(info.Id), EnvelopeFactory.Create(HttpResponseTypes.Asset, new UploadAssetResponse
                {
                    Asset = ToAssetRef(info),
                    Meta = ToAssetMeta(info),
                }));
            }
            catch (InvalidOperationException ex)
            {
                return Error(ErrorCodes.AssetTooLarge, ex.Message);
            }
        }

        app.MapGet(HttpPaths.AssetTemplate, async (string id, HttpContext ctx, CancellationToken requestCt) =>
        {
            var blob = await assets.GetAsync(id, requestCt);
            if (blob is null)
                return Error(ErrorCodes.AssetNotFound, $"asset not found: {id}");

            if (CheckAssetAccess(ctx, blob.Info) is { } denied)
                return denied;

            return Results.File(blob.Bytes, blob.Info.Mime);
        });

        app.MapMethods(HttpPaths.AssetTemplate, ["HEAD"], (string id, HttpContext ctx) =>
        {
            var info = assets.Stat(id);
            if (info is null)
                return Error(ErrorCodes.AssetNotFound, $"asset not found: {id}");
            return CheckAssetAccess(ctx, info) ?? Json(EnvelopeFactory.Create(HttpResponseTypes.Asset, ToAssetMeta(info)));
        });

        app.MapDelete(HttpPaths.AssetTemplate, async (string id, HttpContext ctx) =>
        {
            var info = assets.Stat(id);
            if (info is not null && CheckAssetAccess(ctx, info) is { } denied)
                return denied;
            var deleted = assets.Delete(id);
            await AuditAsync(auditSubscribers, "asset.delete", Actor(ctx), id, deleted ? "ok" : "not_found", null, default);
            return deleted
                ? Json(EnvelopeFactory.Create(HttpResponseTypes.Asset, new DeletedResponse { Id = id }))
                : Error(ErrorCodes.AssetNotFound, $"asset not found: {id}");
        });
    }
}
