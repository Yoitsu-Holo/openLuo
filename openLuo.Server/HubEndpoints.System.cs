using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：鉴权与系统探测）。</summary>
public static partial class HubServer
{
    /// <summary>
    /// 鉴权域：admin/user 权限守卫（**中间件**，必须在端点注册前调用）+ 令牌签发（§4.4/§4.8）。
    /// </summary>
    private static void MapAuthEndpoints(WebApplication app, HubContext hub)
    {
        var tokens = hub.Tokens;
        var auditSubscribers = hub.AuditSubscribers;

        // 权限守卫（§4.8）：admin-only 路径未鉴权 → 3001，越权 → 3002。
        string[] adminPrefixes = ["/v1/config", "/v1/logs", "/v1/metrics", "/v1/schedules", "/v1/traces"];
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? string.Empty;
            var needsAdmin = adminPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                || (path.StartsWith("/v1/jobs", StringComparison.OrdinalIgnoreCase) && !HttpMethods.IsGet(ctx.Request.Method));

            var needsUser = path.StartsWith("/v1/assets", StringComparison.OrdinalIgnoreCase);
            if (needsAdmin || needsUser)
            {
                var role = tokens.Authorize(TokenRegistry.BearerOf(ctx.Request.Headers.Authorization.ToString()));
                if (role is null)
                {
                    await WriteErrorAsync(ctx, ErrorCodes.AuthUnauthorized, "missing or invalid token");
                    return;
                }
                var requiredRank = needsAdmin ? HubRoles.Rank(HubRoles.Admin) : HubRoles.Rank(HubRoles.User);
                if (HubRoles.Rank(role) < requiredRank)
                {
                    await WriteErrorAsync(ctx, ErrorCodes.AuthForbidden, $"requires role: {(needsAdmin ? HubRoles.Admin : HubRoles.User)}");
                    return;
                }
            }
            await next();
        });

        app.MapPost("/v1/auth/token", async (HttpContext ctx, CancellationToken requestCt) =>
        {
            var request = await JsonSerializer.DeserializeAsync<TokenRequest>(ctx.Request.Body, ProtocolJson.Options, requestCt)
                          ?? new TokenRequest();
            var issued = tokens.TryIssue(request, out var response, out var errorCode, out var error);
            if (issued)
                await AuditAsync(auditSubscribers, "auth.token", request.ClientId, response.Role, "ok", null, requestCt);

            return issued
                ? Json(EnvelopeFactory.Create("auth.token", response))
                : Results.Json(
                    EnvelopeFactory.CreateError(EventTypes.Error, errorCode, error),
                    ProtocolJson.Options,
                    statusCode: StatusCodes.Status401Unauthorized);
        });
    }

    /// <summary>系统域：存活 / 版本探测（公开，无需鉴权）。</summary>
    private static void MapSystemEndpoints(WebApplication app, HubContext hub)
    {
        var startedAt = hub.StartedAt;
        var options = hub.Options;
        var features = hub.Features;

        app.MapGet("/v1/health", () => Json(EnvelopeFactory.Create("health", new HealthDto
        {
            Status = "ok",
            UptimeSec = (DateTimeOffset.UtcNow - startedAt).TotalSeconds,
        })));

        app.MapGet("/v1/version", () => Json(EnvelopeFactory.Create("version", new VersionDto
        {
            ServerVersion = options.ServerVersion,
            ProtocolVersion = ProtocolInfo.MajorVersion,
            Features = features,
        })));
    }
}
