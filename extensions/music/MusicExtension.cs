using openLuo.Abstractions;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace OpenLuo.Extensions.Music;

/// <summary>
/// 音乐分享卡扩展：把"分享一首歌"表达为结构化 Card OutputItem（内核不解析）。
///
/// Payload 契约（产出侧与平台渲染侧联合约定，内核零感知）：
///   Platform = "163"（网易云音乐）；Id = 平台资源 id；Title/Url 供文本宿主降级显示。
/// QQ 适配层将 Platform=="163" 的 Card 渲染为 OneBot music 段（type "163"），
/// CLI 等无卡平台按 Url 降级为文本链接。
/// </summary>
public sealed class MusicExtension : IAgentExtension
{
    public void Configure(ExtensionBuilder builder)
    {
        builder.AddCapability(MusicDescriptors.ShareSong, new ShareSongInvoker());
    }
}

internal static class MusicDescriptors
{
    public static CapabilityDescriptor ShareSong => new()
    {
        CanonicalId = "share_song", DisplayName = "Share music card",
        Summary = "Share a song as a rich music card to the current channel.",
        Usage = "Use when the user asks you to play, share, or recommend a specific song and the channel supports music cards. Pass the REAL song id returned by cloud_music_search; never invent an id. Send at most one card per request; after the call, add a short remark or end the turn.",
        Kind = CapabilityKind.Builtin, ProviderId = "music",
        SideEffect = SideEffectClass.ReadOnly, Completion = CompletionPolicy.Continue,
        ParallelSafe = false,
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                id = new { type = "integer", description = "netease song id from cloud_music_search result" },
                platform = new { type = "string", description = "music platform key; default 163 (netease)" },
                title = new { type = "string", description = "optional song title for text fallback rendering" }
            },
            required = new[] { "id" }
        }
    };
}

/// <summary>音乐分享卡 Payload（扩展与平台渲染侧契约；属性名即序列化名，勿改名）。</summary>
public sealed record MusicShareCardPayload
{
    /// <summary>平台标识，如 "163"（网易云音乐）。</summary>
    public string Platform { get; init; } = "163";
    /// <summary>平台内资源 id（网易云 song id）。</summary>
    public long Id { get; init; }
    /// <summary>歌曲名（可选，文本宿主降级显示用）。</summary>
    public string? Title { get; init; }
    /// <summary>网页可访问地址（可选，文本宿主降级显示用；扩展会按平台补全）。</summary>
    public string? Url { get; init; }
}

internal sealed class ShareSongInvoker : ICapabilityInvoker
{
    public Task<CapabilityResult> InvokeAsync(
        CapabilityCall call,
        CapabilityExecutionContext context,
        CancellationToken ct = default)
    {
        var platform = call.Options.GetValueOrDefault("platform", "163");
        if (!long.TryParse(call.Options.GetValueOrDefault("id", string.Empty), out var id))
            return Task.FromResult(new CapabilityResult
            {
                InvocationId = call.InvocationId,
                Success = false,
                Status = CapabilityStatus.Rejected,
                Error = "id is required and must be a numeric song id from cloud_music_search"
            });

        var title = call.Options.TryGetValue("title", out var t) && !string.IsNullOrWhiteSpace(t) ? t : null;
        var url = call.Options.TryGetValue("url", out var u) && !string.IsNullOrWhiteSpace(u)
            ? u
            : platform.Equals("163", StringComparison.OrdinalIgnoreCase)
                ? $"https://music.163.com/#/song?id={id}"
                : null;

        return Task.FromResult(new CapabilityResult
        {
            InvocationId = call.InvocationId,
            Success = true,
            Status = CapabilityStatus.Ok,
            Text = $"shared music card platform={platform} id={id}; card send is complete — do NOT call share_song again for this request",
            Outputs =
            [
                new OutputItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = ReplyItemKind.Card,
                    Payload = new MusicShareCardPayload { Platform = platform, Id = id, Title = title, Url = url },
                    SourceCapability = call.CanonicalId,
                    Fingerprint = $"music-card:{platform}:{id}"
                }
            ]
        });
    }
}
