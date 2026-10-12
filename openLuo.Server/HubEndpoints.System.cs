using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：鉴权与系统探测）。</summary>
public static partial class HubServer
{
    /// <summary>
    /// 路径里出现的资源 id（会话 / 资产 / 轨迹），供入口治理做 id 白名单校验（§4.9）。
    /// 捕获整个路径段（`[^/]+`）后用 <see cref="HttpRules.ResourceIdPattern"/> 校验——
    /// 若只匹配白名单字符会漏判 `bad.id` 这类非法段（匹配在 `.` 处停止，等于放行）。
    /// </summary>
    private static readonly Regex ResourceIdInPath = new(
        $"^{ProtocolInfo.HttpBasePath}/(?:{HttpRules.IdAddressedCollections})/([^/]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 鉴权域：admin/user 权限守卫（**中间件**，必须在端点注册前调用）+ 令牌签发（§4.4/§4.8）。
    /// 最前面还有入口治理：限流 + 路径资源 id 白名单（§4.9）。
    /// </summary>
    private static void MapAuthEndpoints(WebApplication app, HubContext hub)
    {
        var tokens = hub.Tokens;
        var auditSubscribers = hub.AuditSubscribers;

        // 权限守卫（§4.8）：admin-only 路径未鉴权 → 3001，越权 → 3002。
        string[] adminPrefixes = [HttpPaths.Config, HttpPaths.Logs, HttpPaths.Metrics, HttpPaths.Schedules, HttpPaths.Traces];
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? string.Empty;
            var guard = hub.Guard;
            var bearer = TokenRegistry.BearerOf(ctx.Request.Headers.Authorization.ToString());

            // 入口治理（§4.9）：限流（key = token → 客户端 IP），未通过回 429 + 8001。
            var key = string.IsNullOrEmpty(bearer) ? ctx.Connection.RemoteIpAddress?.ToString() : bearer;
            if (!guard.Allow(key))
            {
                await WriteErrorAsync(ctx, ErrorCodes.RateLimited, guard.RateLimitHint);
                return;
            }

            // 入口治理（§4.9）：路径里的资源 id 白名单（会话 / 资产 / 轨迹），挡路径穿越与分隔符注入。
            var idMatch = ResourceIdInPath.Match(path);
            if (idMatch.Success && !guard.ValidResourceId(idMatch.Groups[1].Value))
            {
                await WriteErrorAsync(ctx, ErrorCodes.SessionInvalid,
                    "invalid resource id in path: only letters, digits, '-' and '_' are allowed");
                return;
            }

            var needsAdmin = adminPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                || (path.StartsWith(HttpPaths.Jobs, StringComparison.OrdinalIgnoreCase) && !HttpMethods.IsGet(ctx.Request.Method));

            var needsUser = path.StartsWith(HttpPaths.Assets, StringComparison.OrdinalIgnoreCase);
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

        app.MapPost(HttpPaths.Tokens, async (HttpContext ctx, CancellationToken requestCt) =>
        {
            var request = await JsonSerializer.DeserializeAsync<TokenRequest>(ctx.Request.Body, ProtocolJson.Options, requestCt)
                          ?? new TokenRequest();
            var issued = tokens.TryIssue(request, out var response, out var errorCode, out var error);
            if (issued)
                await AuditAsync(auditSubscribers, "auth.token", request.ClientId, response.Role, "ok", null, requestCt);

            return issued
                ? Created(ctx, EnvelopeFactory.Create(HttpResponseTypes.Token, response))
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

        app.MapGet(HttpPaths.Health, () => Json(EnvelopeFactory.Create(HttpResponseTypes.Health, new HealthDto
        {
            Status = "ok",
            UptimeSec = (DateTimeOffset.UtcNow - startedAt).TotalSeconds,
        })));

        app.MapGet(HttpPaths.Version, () => Json(EnvelopeFactory.Create(HttpResponseTypes.Version, new VersionDto
        {
            ServerVersion = options.ServerVersion,
            ProtocolVersion = ProtocolInfo.MajorVersion,
            Features = features,
        })));
    }
}
