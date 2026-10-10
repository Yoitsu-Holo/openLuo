using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：配置，§5.7）。</summary>
public static partial class HubServer
{
    /// <summary>配置域（§5.7，admin-only）：列举 / 读取 / 合并写入 / 删除覆盖。未接入则整组不注册。</summary>
    private static void MapConfigEndpoints(WebApplication app, HubContext hub)
    {
        var config = hub.Config;
        if (config is null)
            return;

        var auditSubscribers = hub.AuditSubscribers;
        string? Actor(HttpContext ctx) => hub.Actor(ctx);

        app.MapGet("/v1/config", () => Json(EnvelopeFactory.Create("config", new ConfigListResponse
        {
            Namespaces = config.ListNamespaces().Select(n => new ConfigNamespaceDto
            {
                Namespace = n.Namespace,
                Source = n.Source,
                Overridden = n.Overridden,
                UpdatedAt = n.UpdatedAt,
            }).ToList(),
        })));

        app.MapGet("/v1/config/{ns}", async (string ns, CancellationToken requestCt) =>
        {
            var view = await config.GetAsync(ns, requestCt);
            return view is null
                ? Error(ErrorCodes.ConfigNamespaceNotFound, $"config namespace not found: {ns}")
                : Json(EnvelopeFactory.Create("config", new ConfigGetResponse
                {
                    Namespace = view.Namespace,
                    Source = view.Source,
                    Values = Mask(view.Values),
                    Overrides = Mask(view.Overrides),
                }));
        });

        app.MapPost("/v1/config/{ns}", async (string ns, HttpContext ctx, CancellationToken requestCt) =>
        {
            var body = await JsonSerializer.DeserializeAsync<ConfigSetRequest>(ctx.Request.Body, ProtocolJson.Options, requestCt)
                       ?? new ConfigSetRequest();
            if (body.Values is null)
                return Error(ErrorCodes.ConfigInvalidValue, "values is required");

            var view = await config.SetAsync(ns, body.Values, body.Persist, requestCt);
            await AuditAsync(auditSubscribers, "config.set", Actor(ctx), ns, "ok", null, requestCt);
            return Json(EnvelopeFactory.Create("config", new ConfigSetResponse
            {
                Namespace = view.Namespace,
                Source = view.Source,
                Values = Mask(view.Values),
            }));
        });

        app.MapDelete("/v1/config/{ns}", async (string ns, HttpContext ctx, CancellationToken requestCt) =>
        {
            var persist = string.Equals(ctx.Request.Query["persist"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            var view = await config.DeleteAsync(ns, persist, requestCt);
            await AuditAsync(auditSubscribers, "config.delete", Actor(ctx), ns, view is null ? "not_found" : "ok", null, requestCt);
            return view is null
                ? Error(ErrorCodes.ConfigNamespaceNotFound, $"config namespace not found: {ns}")
                : Json(EnvelopeFactory.Create("config", new ConfigDeleteResponse
                {
                    Namespace = view.Namespace,
                    Source = view.Source,
                    Values = Mask(view.Values),
                }));
        });
    }
}
