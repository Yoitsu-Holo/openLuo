using Microsoft.AspNetCore.Builder;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>HubServer 的 HTTP 控制面端点，按域分文件（本文件：能力目录）。</summary>
public static partial class HubServer
{
    /// <summary>能力目录域（§5.3）：依赖 <see cref="IRuntimeDirectory"/>，未接入则不注册该端点。</summary>
    private static void MapCapabilityEndpoints(WebApplication app, HubContext hub)
    {
        var directory = hub.Directory;
        if (directory is null)
            return;

        app.MapGet("/v1/capabilities", async (string? sessionId, CancellationToken requestCt) =>
        {
            var capabilities = await directory.ListCapabilitiesAsync(sessionId, requestCt);
            return Json(EnvelopeFactory.Create("capabilities", new CapabilitiesResponse
            {
                Version = 1,
                Capabilities = capabilities.Select(WireMapper.ToDto).ToList(),
            }));
        });
    }
}
